﻿using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.Graphics.Drawing;
using Robust.Client.Input;
using Robust.Client.Interfaces.UserInterface;
using Robust.Client.Utility;
using Robust.Shared.IoC;
using Robust.Shared.Maths;

namespace Robust.Client.UserInterface.Controls
{
    [ControlWrap("LineEdit")]
    public class LineEdit : Control
    {
        public const string StylePropertyStyleBox = "stylebox";
        public const string StyleClassLineEditNotEditable = "notEditable";
        public const string StylePseudoClassPlaceholder = "placeholder";

        [NotNull] private string _text = "";
        private bool _editable = true;
        [CanBeNull] private string _placeHolder;
        private int _cursorPosition;
        private float _cursorBlinkTimer;
        private bool _cursorCurrentlyLit;
        private const float BlinkTime = 0.5f;

        private bool IsPlaceHolderVisible => string.IsNullOrEmpty(_text) && _placeHolder != null;

        public LineEdit()
        {
        }

        public LineEdit(string name) : base(name)
        {
        }

        public AlignMode TextAlign { get; set; }

        public string Text
        {
            get => _text;
            set
            {
                if (value == null)
                {
                    value = "";
                }

                _text = value;
                _cursorPosition = 0;
                _updatePseudoClass();
            }
        }

        public bool Editable
        {
            get => _editable;
            set
            {
                _editable = value;
                if (!_editable)
                {
                    AddStyleClass(StyleClassLineEditNotEditable);
                }
                else
                {
                    RemoveStyleClass(StyleClassLineEditNotEditable);
                }
            }
        }

        public string PlaceHolder
        {
            get => _placeHolder;
            set
            {
                _placeHolder = value;
                _updatePseudoClass();
            }
        }

        public bool IgnoreNext { get; set; }

        // TODO:
        // I decided to not implement the entire LineEdit API yet,
        // since most of it won't be used yet (if at all).
        // Feel free to implement wrappers for all the other properties!
        // Future me reporting, thanks past me.
        // Second future me reporting, thanks again.
        // Third future me is here to say thanks.
        // Fourth future me is here to continue the tradition.

        public void Clear()
        {
            Text = "";
        }

        public void Select(int from = 0, int to = -1)
        {
        }

        public void SelectAll()
        {
        }

        public event Action<LineEditEventArgs> OnTextChanged;
        public event Action<LineEditEventArgs> OnTextEntered;

        public void InsertAtCursor(string text)
        {
            // Strip newlines.
            var chars = new List<char>(text.Length);
            foreach (var chr in text)
            {
                if (chr == '\n')
                {
                    continue;
                }
                chars.Add(chr);
            }

            if (chars.Count == 0)
            {
                return;
            }

            _text = _text.Insert(_cursorPosition, new string(chars.ToArray()));
            _cursorPosition += chars.Count;
            OnTextChanged?.Invoke(new LineEditEventArgs(this, _text));
            _updatePseudoClass();
        }

        protected internal override void Draw(DrawingHandleScreen handle)
        {
            var styleBox = _getStyleBox();
            var drawBox = PixelSizeBox;
            var contentBox = styleBox.GetContentBox(drawBox);
            styleBox.Draw(handle, drawBox);
            var font = _getFont();
            var renderedTextColor = _getFontColor();

            var offsetY = (int) (contentBox.Height - font.GetHeight(UIScale)) / 2;
            var baseLine = new Vector2i(0, offsetY + font.GetAscent(UIScale)) + contentBox.TopLeft;

            string renderedText;

            if (IsPlaceHolderVisible)
            {
                renderedText = _placeHolder;
            }
            else
            {
                renderedText = _text;
            }

            float? actualCursorPosition = null;

            if (_cursorPosition == 0)
            {
                actualCursorPosition = contentBox.Left;
            }

            var count = 0;
            foreach (var chr in renderedText)
            {
                if (!font.TryGetCharMetrics(chr, UIScale, out var metrics))
                {
                    count += 1;
                    continue;
                }

                // Glyph would be outside the bounding box, abort.
                if (baseLine.X + metrics.Width + metrics.BearingX > contentBox.Right)
                {
                    break;
                }

                font.DrawChar(handle, chr, baseLine, UIScale, renderedTextColor);
                baseLine += new Vector2(metrics.Advance, 0);
                count += 1;
                if (count == _cursorPosition)
                {
                    actualCursorPosition = baseLine.X;
                }
            }

            if (_cursorCurrentlyLit && actualCursorPosition.HasValue && HasKeyboardFocus())
            {
                handle.DrawRect(
                    new UIBox2(actualCursorPosition.Value, contentBox.Top, actualCursorPosition.Value + 1,
                        contentBox.Bottom), Color.White);
            }
        }

        protected override void FrameUpdate(RenderFrameEventArgs args)
        {
            base.FrameUpdate(args);

            _cursorBlinkTimer -= args.Elapsed;
            if (_cursorBlinkTimer <= 0)
            {
                _cursorBlinkTimer += BlinkTime;
                _cursorCurrentlyLit = !_cursorCurrentlyLit;
            }
        }

        protected override Vector2 CalculateMinimumSize()
        {
            var font = _getFont();
            var style = _getStyleBox();
            return new Vector2(0, font.GetHeight(UIScale)/UIScale) + style.MinimumSize/UIScale;
        }

        protected internal override void TextEntered(GUITextEventArgs args)
        {
            base.TextEntered(args);

            if (!Editable)
            {
                return;
            }

            if (IgnoreNext)
            {
                IgnoreNext = false;
                return;
            }

            _text = _text.Insert(_cursorPosition, ((char) args.CodePoint).ToString());
            _cursorPosition += 1;
            OnTextChanged?.Invoke(new LineEditEventArgs(this, _text));
            _updatePseudoClass();
        }

        protected internal override void KeyDown(GUIKeyEventArgs args)
        {
            base.KeyDown(args);

            // Just eat all keyboard input.
            args.Handle();

            switch (args.Key)
            {
                case Keyboard.Key.BackSpace:
                    if (_cursorPosition == 0 || !Editable)
                    {
                        return;
                    }

                    _text = _text.Remove(_cursorPosition - 1, 1);
                    OnTextChanged?.Invoke(new LineEditEventArgs(this, _text));
                    _cursorPosition -= 1;
                    _updatePseudoClass();
                    break;

                case Keyboard.Key.Left:
                    if (_cursorPosition == 0)
                    {
                        return;
                    }

                    _cursorPosition -= 1;
                    break;

                case Keyboard.Key.Right:
                    if (_cursorPosition == _text.Length)
                    {
                        return;
                    }

                    _cursorPosition += 1;
                    break;

                case Keyboard.Key.NumpadEnter:
                case Keyboard.Key.Return:
                    if (Editable)
                    {
                        OnTextEntered?.Invoke(new LineEditEventArgs(this, _text));
                    }

                    break;

                case Keyboard.Key.V:
                    if (Editable && args.Control)
                    {
                        var clipboard = IoCManager.Resolve<IClipboardManager>();
                        if (!clipboard.Available)
                        {
                            UserInterfaceManager.Popup(clipboard.NotAvailableReason, "Clipboard not available!");
                            return;
                        }

                        InsertAtCursor(clipboard.GetText());
                    }

                    break;
            }
        }

        protected internal override void MouseDown(GUIMouseButtonEventArgs args)
        {
            base.MouseDown(args);

            // Find closest cursor position under mouse.
            var style = _getStyleBox();
            var contentBox = style.GetContentBox(PixelSizeBox);

            var clickPosX = args.RelativePosition.X * UIScale;

            var font = _getFont();
            var index = 0;
            var chrPosX = contentBox.Left;
            var lastChrPostX = contentBox.Left;
            foreach (var chr in _text)
            {
                if (!font.TryGetCharMetrics(chr, UIScale, out var metrics))
                {
                    index += 1;
                    continue;
                }

                if (chrPosX > clickPosX)
                {
                    break;
                }

                lastChrPostX = chrPosX;
                chrPosX += metrics.Advance;
                index += 1;

                if (chrPosX > contentBox.Right)
                {
                    break;
                }
            }

            // Distance between the right side of the glyph overlapping the mouse and the mouse.
            var distanceRight = chrPosX - clickPosX;
            // Same but left side.
            var distanceLeft = clickPosX - lastChrPostX;
            // If the mouse is closer to the left of the glyph we lower the index one, so we select before that glyph.
            if (distanceRight > distanceLeft)
            {
                index -= 1;
            }

            _cursorPosition = index;

            // Reset this so the cursor is always visible immediately after a click.
            _cursorCurrentlyLit = true;
            _cursorBlinkTimer = BlinkTime;
        }

        protected internal override void FocusEntered()
        {
            base.FocusEntered();

            // Reset this so the cursor is always visible immediately after gaining focus..
            _cursorCurrentlyLit = true;
            _cursorBlinkTimer = BlinkTime;
        }

        protected override void SetDefaults()
        {
            base.SetDefaults();

            MouseFilter = MouseFilterMode.Stop;
            CanKeyboardFocus = true;
            KeyboardFocusOnClick = true;
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
        private StyleBox _getStyleBox()
        {
            if (TryGetStyleProperty(StylePropertyStyleBox, out StyleBox box))
            {
                return box;
            }

            return UserInterfaceManager.ThemeDefaults.LineEditBox;
        }

        [Pure]
        private Color _getFontColor()
        {
            if (TryGetStyleProperty("font-color", out Color color))
            {
                return color;
            }

            return Color.White;
        }

        private void _updatePseudoClass()
        {
            StylePseudoClass = IsPlaceHolderVisible ? StylePseudoClassPlaceholder : null;
        }

        public enum AlignMode
        {
            Left = 0,
            Center = 1,
            Right = 2,
        }

        public class LineEditEventArgs : EventArgs
        {
            public LineEdit Control { get; }
            public string Text { get; }

            public LineEditEventArgs(LineEdit control, string text)
            {
                Control = control;
                Text = text;
            }
        }

        private protected override void SetGodotProperty(string property, object value, GodotAssetScene context)
        {
            base.SetGodotProperty(property, value, context);

            if (property == "text")
            {
                Text = (string) value;
            }
            else if (property == "placeholder_text")
            {
                PlaceHolder = (string) value;
            }
            else if (property == "editable")
            {
                Editable = (bool) value;
            }
        }
    }
}
