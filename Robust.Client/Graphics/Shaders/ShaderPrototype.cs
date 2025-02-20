﻿using Robust.Client.Interfaces.ResourceManagement;
using Robust.Client.ResourceManagement;
using Robust.Client.ResourceManagement.ResourceTypes;
using Robust.Client.Utility;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using System;
using System.Collections.Generic;
using Robust.Client.Interfaces.Graphics;
using YamlDotNet.RepresentationModel;

namespace Robust.Client.Graphics.Shaders
{
    [Prototype("shader")]
    public sealed class ShaderPrototype : IPrototype, IIndexedPrototype
    {
        public string ID { get; private set; }

        private ShaderKind Kind;

        // Source shader variables.
        private ShaderSourceResource Source;
        private Dictionary<string, object> ShaderParams;

        // Canvas shader variables.
        private LightModeEnum LightMode;
#pragma warning disable 414
        // TODO: Use this.
        private BlendModeEnum BlendMode;
#pragma warning restore 414

        private Shader _canvasKindInstance;

        /// <summary>
        ///     Creates a new instance of this shader.
        /// </summary>
        public Shader Instance()
        {
            switch (Kind)
            {
                case ShaderKind.Source:
                    return new Shader(Source.ClydeHandle);

                case ShaderKind.Canvas:
                    if (_canvasKindInstance != null)
                    {
                        return _canvasKindInstance;
                    }

                    string source;
                    if (LightMode == LightModeEnum.Unshaded)
                    {
                        source = SourceUnshaded;
                    }
                    else
                    {
                        source = SourceShaded;
                    }

                    var parsed = ShaderParser.Parse(source);
                    var clyde = IoCManager.Resolve<IClyde>();
                    var instance = new Shader(clyde.LoadShader(parsed));
                    _canvasKindInstance = instance;
                    return instance;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void LoadFrom(YamlMappingNode mapping)
        {
            ID = mapping.GetNode("id").ToString();

            var kind = mapping.GetNode("kind").AsString();
            switch (kind)
            {
                case "source":
                    Kind = ShaderKind.Source;
                    ReadSourceKind(mapping);
                    break;

                case "canvas":
                    Kind = ShaderKind.Canvas;
                    ReadCanvasKind(mapping);
                    break;

                default:
                    throw new InvalidOperationException($"Invalid shader kind: '{kind}'");
            }
        }

        private void ReadSourceKind(YamlMappingNode mapping)
        {
            var path = mapping.GetNode("path").AsResourcePath();
            var resc = IoCManager.Resolve<IResourceCache>();
            Source = resc.GetResource<ShaderSourceResource>(path);
            if (mapping.TryGetNode<YamlMappingNode>("params", out var paramMapping))
            {
                ShaderParams = new Dictionary<string, object>();
                foreach (var item in paramMapping)
                {
                    var name = item.Key.AsString();
                    // TODO: This.
                    if (true)
                    //if (!Source.TryGetShaderParamType(name, out var type))
                    {
                        Logger.ErrorS("shader", "Shader param '{0}' does not exist on shader '{1}'", name, path);
                        continue;
                    }

                    //var value = ParseShaderParamFor(item.Value, type);
                    //ShaderParams.Add(name, value);
                }
            }
        }

        private void ReadCanvasKind(YamlMappingNode mapping)
        {
            if (mapping.TryGetNode("light_mode", out var node))
            {
                switch (node.AsString())
                {
                    case "normal":
                        LightMode = LightModeEnum.Normal;
                        break;

                    case "unshaded":
                        LightMode = LightModeEnum.Unshaded;
                        break;

                    case "light_only":
                        LightMode = LightModeEnum.LightOnly;
                        break;

                    default:
                        throw new InvalidOperationException($"Invalid light mode: '{node.AsString()}'");
                }
            }

            if (mapping.TryGetNode("blend_mode", out node))
            {
                switch (node.AsString())
                {
                    case "mix":
                        BlendMode = BlendModeEnum.Mix;
                        break;

                    case "add":
                        BlendMode = BlendModeEnum.Add;
                        break;

                    case "subtract":
                        BlendMode = BlendModeEnum.Subtract;
                        break;

                    case "multiply":
                        BlendMode = BlendModeEnum.Multiply;
                        break;

                    case "premultiplied_alpha":
                        BlendMode = BlendModeEnum.PremultipliedAlpha;
                        break;

                    default:
                        throw new InvalidOperationException($"Invalid blend mode: '{node.AsString()}'");
                }
            }
        }

        enum ShaderKind
        {
            Source,
            Canvas
        }

        private const string SourceUnshaded = @"
render_mode unshaded;
void fragment() {
    COLOR = texture(TEXTURE, UV);
}
";

        private const string SourceShaded = @"
void fragment() {
    COLOR = texture(TEXTURE, UV);
}
";

        private enum LightModeEnum
        {
            Normal = 0,
            Unshaded = 1,
            LightOnly = 2,
        }

        private enum BlendModeEnum
        {
            Mix = 0,
            Add = 1,
            Subtract = 2,
            Multiply = 3,
            PremultipliedAlpha = 4,
        }
    }
}
