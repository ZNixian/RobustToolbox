using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.Graphics.Drawing;
using Robust.Client.Input;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using PureAttribute = System.Diagnostics.Contracts.PureAttribute;

namespace Robust.Client.UserInterface.Controls
{
    /// <summary>
    ///     A control to handle output of message-by-message output panels, like the debug console and chat panel.
    /// </summary>
    public class OutputPanel : Control
    {
        public const string StylePropertyStyleBox = "stylebox";

        private readonly List<RichTextEntry> _entries = new List<RichTextEntry>();
        private bool _isAtBottom = true;

        private int _totalContentHeight;
        private bool _firstLine = true;
        private StyleBox _styleBoxOverride;
        private VScrollBar _scrollBar;

        public bool ScrollFollowing { get; set; } = true;

        public StyleBox StyleBoxOverride
        {
            get => _styleBoxOverride;
            set
            {
                _styleBoxOverride = value;
                MinimumSizeChanged();
                _invalidateEntries();
            }
        }

        public void Clear()
        {
            _firstLine = true;
            _entries.Clear();
            _totalContentHeight = 0;
            _scrollBar.MaxValue = Math.Max(_scrollBar.Page, _totalContentHeight);
            _scrollBar.Value = 0;
        }

        public void RemoveLine(int line)
        {
            var entry = _entries[line];
            _entries.RemoveAt(line);

            var font = _getFont();
            _totalContentHeight -= entry.Height + font.GetLineSeparation(UIScale);
            if (_entries.Count == 0)
            {
                Clear();
            }
            _scrollBar.MaxValue = Math.Max(_scrollBar.Page, _totalContentHeight);
        }

        public void AddMessage(FormattedMessage message)
        {
            var entry = new RichTextEntry(message);

            entry.Update(_getFont(), _getContentBox().Width, UIScale);

            _entries.Add(entry);
            var font = _getFont();
            _totalContentHeight += entry.Height;
            if (_firstLine)
            {
                _firstLine = false;
            }
            else
            {
                _totalContentHeight += font.GetLineSeparation(UIScale);
            }

            _scrollBar.MaxValue = Math.Max(_scrollBar.Page, _totalContentHeight);
            if (_isAtBottom && ScrollFollowing)
            {
                _scrollBar.MoveToEnd();
            }
        }

        public void ScrollToBottom()
        {
            _scrollBar.MoveToEnd();
            _isAtBottom = true;
        }

        protected override void Initialize()
        {
            base.Initialize();

            _scrollBar = new VScrollBar {Name = "_v_scroll"};
            AddChild(_scrollBar);
            _scrollBar.SetAnchorAndMarginPreset(LayoutPreset.RightWide);
            _scrollBar.OnValueChanged += _ => _isAtBottom = _scrollBar.IsAtEnd;
        }

        protected internal override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);

            var style = _getStyleBox();
            var font = _getFont();
            style?.Draw(handle, PixelSizeBox);
            var contentBox = _getContentBox();

            var entryOffset = -_scrollBar.Value;

            // A stack for format tags.
            // This stack contains the format tag to RETURN TO when popped off.
            // So when a new color tag gets hit this stack gets the previous color pushed on.
            var formatStack = new Stack<FormattedMessage.Tag>(2);

            foreach (var entry in _entries)
            {
                if (entryOffset + entry.Height < 0)
                {
                    entryOffset += entry.Height + font.GetLineSeparation(UIScale);
                    continue;
                }

                if (entryOffset > contentBox.Height)
                {
                    break;
                }

                entry.Draw(handle, font, contentBox, entryOffset, formatStack, UIScale);

                entryOffset += entry.Height + font.GetLineSeparation(UIScale);
            }
        }

        protected internal override void MouseWheel(GUIMouseWheelEventArgs args)
        {
            base.MouseWheel(args);

            if (args.WheelDirection == Mouse.Wheel.Up)
            {
                _scrollBar.Value -= _getScrollSpeed();
                _isAtBottom = false;
            }
            else if (args.WheelDirection == Mouse.Wheel.Down)
            {
                _scrollBar.Value += _getScrollSpeed();
                if (_scrollBar.IsAtEnd)
                {
                    _isAtBottom = true;
                }
            }
        }

        protected override void SetDefaults()
        {
            base.SetDefaults();

            RectClipContent = true;
        }

        protected override void Resized()
        {
            base.Resized();

            var styleBoxSize = _getStyleBox()?.MinimumSize.Y ?? 0;

            _scrollBar.Page = PixelSize.Y - styleBoxSize;
            _invalidateEntries();
        }

        protected override Vector2 CalculateMinimumSize()
        {
            return _getStyleBox()?.MinimumSize ?? Vector2.Zero;
        }

        private void _invalidateEntries()
        {
            _totalContentHeight = 0;
            var font = _getFont();
            var sizeX = _getContentBox().Width;
            for (var i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                entry.Update(font, sizeX, UIScale);
                _entries[i] = entry;
                _totalContentHeight += entry.Height + font.GetLineSeparation(UIScale);
            }

            _scrollBar.MaxValue = Math.Max(_scrollBar.Page, _totalContentHeight);
            if (_isAtBottom && ScrollFollowing)
            {
                _scrollBar.MoveToEnd();
            }
        }

        [Pure]
        private Font _getFont()
        {
            if (TryGetStyleProperty("font", out Font font))
            {
                return font;
            }

            return UserInterfaceManager.ThemeDefaults.DefaultFont;
        }

        [Pure]
        [CanBeNull]
        private StyleBox _getStyleBox()
        {
            if (StyleBoxOverride != null)
            {
                return StyleBoxOverride;
            }

            TryGetStyleProperty(StylePropertyStyleBox, out StyleBox box);
            return box;
        }

        [Pure]
        private int _getScrollSpeed()
        {
            var font = _getFont();
            return font.GetLineHeight(UIScale) * 2;
        }

        [Pure]
        private UIBox2 _getContentBox()
        {
            var style = _getStyleBox();
            return style?.GetContentBox(PixelSizeBox) ?? PixelSizeBox;
        }

        protected internal override void UIScaleChanged()
        {
            _invalidateEntries();

            base.UIScaleChanged();
        }
    }
}
