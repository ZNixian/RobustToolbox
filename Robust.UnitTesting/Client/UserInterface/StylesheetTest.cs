using NUnit.Framework;
using Robust.Client.Interfaces.UserInterface;
using Robust.Client.UserInterface;
using Robust.Shared.IoC;
using Robust.Client.UserInterface.Controls;

namespace Robust.UnitTesting.Client.UserInterface
{
    [TestFixture]
    public class StylesheetTest : RobustUnitTest
    {
        public override UnitTestProject Project => UnitTestProject.Client;

        [Test]
        public void TestSelectors()
        {
            var selectorElementLabel = new SelectorElement(typeof(Label), null, null, null);

            var label = new Label();
            var panel = new Panel {StyleIdentifier = "bar"};
            Assert.That(selectorElementLabel.Matches(label), Is.True);
            Assert.That(selectorElementLabel.Matches(panel), Is.False);

            selectorElementLabel = new SelectorElement(typeof(Label), new []{"foo"}, null, null);
            Assert.That(selectorElementLabel.Matches(label), Is.False);
            Assert.That(selectorElementLabel.Matches(panel), Is.False);

            Assert.That(label.HasStyleClass("foo"), Is.False);
            label.AddStyleClass("foo");
            Assert.That(selectorElementLabel.Matches(label), Is.True);
            Assert.That(label.HasStyleClass("foo"));
            // Make sure it doesn't throw.
            label.AddStyleClass("foo");
            label.RemoveStyleClass("foo");
            Assert.That(selectorElementLabel.Matches(label), Is.False);
            Assert.That(label.HasStyleClass("foo"), Is.False);
            // Make sure it doesn't throw.
            label.RemoveStyleClass("foo");

            selectorElementLabel = new SelectorElement(null, null, "bar", null);
            Assert.That(selectorElementLabel.Matches(label), Is.False);
            Assert.That(selectorElementLabel.Matches(panel), Is.True);
        }

        [Test]
        public void TestStyleProperties()
        {
            var sheet = new Stylesheet(new []
            {
                new StyleRule(new SelectorElement(typeof(Label), null, "baz", null), new []
                {
                    new StyleProperty("foo", "honk"),
                }),
                new StyleRule(new SelectorElement(typeof(Label), null, null, null), new []
                {
                    new StyleProperty("foo", "heh"),
                }),
                new StyleRule(new SelectorElement(typeof(Label), null, null, null), new []
                {
                    new StyleProperty("foo", "bar"),
                }),
            });

            var uiMgr = IoCManager.Resolve<IUserInterfaceManager>();
            uiMgr.Stylesheet = sheet;

            var control = new Label();
            control.TryGetStyleProperty("foo", out string value);
            Assert.That(value, Is.EqualTo("bar"));

            control.StyleIdentifier = "baz";
            control.TryGetStyleProperty("foo", out value);
            Assert.That(value, Is.EqualTo("honk"));
        }
    }
}
