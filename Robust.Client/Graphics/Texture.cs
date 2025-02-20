﻿using System;
using System.IO;
using JetBrains.Annotations;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Robust.Client.Interfaces.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using YamlDotNet.RepresentationModel;

namespace Robust.Client.Graphics
{
    /// <summary>
    ///     Contains a texture used for drawing things.
    /// </summary>
    [PublicAPI]
    public abstract class Texture : IDirectionalTextureProvider
    {
        /// <summary>
        ///     The width of the texture, in pixels.
        /// </summary>
        public abstract int Width { get; }

        /// <summary>
        ///     The height of the texture, in pixels.
        /// </summary>
        public abstract int Height { get; }

        public Vector2i Size => new Vector2i(Width, Height);

        public static Texture Transparent { get; internal set; }
        public static Texture White { get; internal set; }

        /// <summary>
        ///     Loads a new texture an existing image.
        /// </summary>
        /// <param name="image">The image to load.</param>
        /// <param name="name">The "name" of this texture. This can be referred to later to aid debugging.</param>
        /// <param name="loadParameters">
        ///     Parameters that influence the loading of textures.
        ///     Defaults to <see cref="TextureLoadParameters.Default"/> if <c>null</c>.
        /// </param>
        /// <typeparam name="T">The type of pixels of the image. At the moment, images must be <see cref="Rgba32"/>.</typeparam>
        public static Texture LoadFromImage<T>(Image<T> image, string name = null,
            TextureLoadParameters? loadParameters = null) where T : unmanaged, IPixel<T>
        {
            var manager = IoCManager.Resolve<IClyde>();
            return manager.LoadTextureFromImage(image, name, loadParameters);
        }

        /// <summary>
        ///     Loads an image from a stream containing PNG data.
        /// </summary>
        /// <param name="stream">The stream to load the image from.</param>
        /// <param name="name">The "name" of this texture. This can be referred to later to aid debugging.</param>
        /// <param name="loadParameters">
        ///     Parameters that influence the loading of textures.
        ///     Defaults to <see cref="TextureLoadParameters.Default"/> if <c>null</c>.
        /// </param>
        public static Texture LoadFromPNGStream(Stream stream, string name = null,
            TextureLoadParameters? loadParameters = null)
        {
            var manager = IoCManager.Resolve<IClyde>();
            return manager.LoadTextureFromPNGStream(stream, name, loadParameters);
        }

        Texture IDirectionalTextureProvider.Default => this;

        Texture IDirectionalTextureProvider.TextureFor(Direction dir)
        {
            return this;
        }
    }

    internal sealed class DummyTexture : Texture
    {
        public override int Width { get; }
        public override int Height { get; }

        public DummyTexture(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    ///     Represents a sub region of another texture.
    ///     This can be a useful optimization in many cases.
    /// </summary>
    [PublicAPI]
    public sealed class AtlasTexture : Texture
    {
        public AtlasTexture(Texture texture, UIBox2 subRegion)
        {
            DebugTools.Assert(SubRegion.Right < texture.Width);
            DebugTools.Assert(SubRegion.Bottom < texture.Height);
            DebugTools.Assert(SubRegion.Left >= 0);
            DebugTools.Assert(SubRegion.Top >= 0);

            SubRegion = subRegion;
            SourceTexture = texture;
        }

        /// <summary>
        ///     The texture this texture is a sub region of.
        /// </summary>
        public Texture SourceTexture { get; }

        /// <summary>
        ///     Our sub region within our source, in pixel coordinates.
        /// </summary>
        public UIBox2 SubRegion { get; }

        public override int Width => (int) SubRegion.Width;
        public override int Height => (int) SubRegion.Height;
    }

    /// <summary>
    ///     Flags for loading of textures.
    /// </summary>
    [PublicAPI]
    public struct TextureLoadParameters
    {
        /// <summary>
        ///     The default sampling parameters for the texture.
        /// </summary>
        public TextureSampleParameters SampleParameters { get; set; }

        public static TextureLoadParameters FromYaml(YamlMappingNode yaml)
        {
            if (yaml.TryGetNode("sample", out YamlMappingNode sampleNode))
            {
                return new TextureLoadParameters {SampleParameters = TextureSampleParameters.FromYaml(sampleNode)};
            }

            return Default;
        }

        public static readonly TextureLoadParameters Default = new TextureLoadParameters
        {
            SampleParameters = TextureSampleParameters.Default
        };
    }

    /// <summary>
    ///     Sample flags for textures.
    ///     These are separate from <see cref="TextureLoadParameters"/>,
    ///     because it is possible to create "proxies" to existing textures
    ///     with different sampling parameters than the base texture.
    /// </summary>
    [PublicAPI]
    public struct TextureSampleParameters
    {
        // NOTE: If somebody is gonna add support for 3D/1D textures, change this doc comment.
        // See the note on this page for why: https://www.khronos.org/opengl/wiki/Sampler_Object#Filtering
        /// <summary>
        ///     If true, use bi-linear texture filtering if the texture cannot be rendered 1:1
        /// </summary>
        public bool Filter { get; set; }

        /// <summary>
        ///     Controls how to wrap the texture if texture coordinates outside 0-1 are accessed.
        /// </summary>
        public TextureWrapMode WrapMode { get; set; }

        public static TextureSampleParameters FromYaml(YamlMappingNode node)
        {
            var wrap = TextureWrapMode.None;
            var filter = false;

            if (node.TryGetNode("filter", out var filterNode))
            {
                filter = filterNode.AsBool();
            }

            if (node.TryGetNode("wrap", out var wrapNode))
            {
                switch (wrapNode.AsString())
                {
                    case "none":
                        wrap = TextureWrapMode.None;
                        break;
                    case "repeat":
                        wrap = TextureWrapMode.Repeat;
                        break;
                    case "mirrored_repeat":
                        wrap = TextureWrapMode.MirroredRepeat;
                        break;
                    default:
                        throw new ArgumentException("Not a valid wrap mode.");
                }
            }

            return new TextureSampleParameters {Filter = filter, WrapMode = wrap};
        }

        public static readonly TextureSampleParameters Default = new TextureSampleParameters
        {
            Filter = false,
            WrapMode = TextureWrapMode.None
        };
    }

    /// <summary>
    ///     Controls behavior when reading texture coordinates outside 0-1, which usually wraps the texture somehow.
    /// </summary>
    [PublicAPI]
    public enum TextureWrapMode
    {
        /// <summary>
        ///     Do not wrap, instead clamp to edge.
        /// </summary>
        None = 0,

        /// <summary>
        ///     Repeat the texture.
        /// </summary>
        Repeat,

        /// <summary>
        ///     Repeat the texture mirrored.
        /// </summary>
        MirroredRepeat,
    }

    internal class OpenGLTexture : Texture
    {
        internal int OpenGLTextureId { get; }
        internal int ArrayIndex { get; }

        public override int Width { get; }
        public override int Height { get; }

        internal OpenGLTexture(int id, int width, int height)
        {
            OpenGLTextureId = id;
            Width = width;
            Height = height;
        }

        internal OpenGLTexture(int id, int width, int height, int arrayIndex)
        {
            OpenGLTextureId = id;
            Width = width;
            Height = height;
            ArrayIndex = arrayIndex;
        }
    }
}
