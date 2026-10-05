using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace Pickets.Controls
{
    /// <summary>
    /// A fence's items for screen readers: a list named after the fence whose entries are the
    /// tiles, each read by its name ("Budget.xlsx, 3 of 12, selected"). A plain ItemsControl
    /// exposes nothing useful, so the fence uses this one and its tiles take keyboard focus.
    /// </summary>
    public class FenceItemsControl : ItemsControl
    {
        protected override DependencyObject GetContainerForItemOverride() => new FenceTile();
        protected override bool IsItemItsOwnContainerOverride(object item) => item is FenceTile;
        protected override AutomationPeer OnCreateAutomationPeer() => new FenceListPeer(this);

        private sealed class FenceListPeer : FrameworkElementAutomationPeer
        {
            public FenceListPeer(FenceItemsControl owner) : base(owner) { }
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;
            protected override string GetClassNameCore() => "FenceItems";
        }
    }

    /// <summary>One tile (or list row). Focusable so arrow keys can move a screen reader along.</summary>
    public class FenceTile : ContentControl
    {
        public FenceTile()
        {
            Focusable = true;
            IsTabStop = false;
            FocusVisualStyle = null; // the selection highlight already shows where you are
        }

        internal FenceItem? Item => DataContext as FenceItem;

        protected override AutomationPeer OnCreateAutomationPeer() => new FenceTilePeer(this);

        private sealed class FenceTilePeer : FrameworkElementAutomationPeer, ISelectionItemProvider
        {
            public FenceTilePeer(FenceTile owner) : base(owner) { }

            private FenceTile Tile => (FenceTile)Owner;

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
            protected override string GetClassNameCore() => "FenceTile";
            protected override string GetNameCore() => Tile.Item?.DisplayName ?? "";
            protected override string GetHelpTextCore() => Tile.Item?.Details ?? "";
            protected override bool IsKeyboardFocusableCore() => true;
            protected override List<AutomationPeer>? GetChildrenCore() => null; // the name says it all

            protected override int GetPositionInSetCore()
            {
                var list = ItemsControl.ItemsControlFromItemContainer(Tile);
                return list == null ? -1 : list.ItemContainerGenerator.IndexFromContainer(Tile) + 1;
            }

            protected override int GetSizeOfSetCore() =>
                ItemsControl.ItemsControlFromItemContainer(Tile)?.Items.Count ?? -1;

            public override object? GetPattern(PatternInterface pattern) =>
                pattern == PatternInterface.SelectionItem ? this : base.GetPattern(pattern);

            bool ISelectionItemProvider.IsSelected => Tile.Item?.IsSelected == true;

            IRawElementProviderSimple? ISelectionItemProvider.SelectionContainer
            {
                get
                {
                    var list = ItemsControl.ItemsControlFromItemContainer(Tile);
                    var peer = list == null ? null : UIElementAutomationPeer.CreatePeerForElement(list);
                    return peer == null ? null : ProviderFromPeer(peer);
                }
            }

            void ISelectionItemProvider.Select()
            {
                var list = ItemsControl.ItemsControlFromItemContainer(Tile);
                if (list != null)
                    foreach (var o in list.Items)
                        if (o is FenceItem i) i.IsSelected = ReferenceEquals(i, Tile.Item);
            }

            void ISelectionItemProvider.AddToSelection() { if (Tile.Item is { } i) i.IsSelected = true; }
            void ISelectionItemProvider.RemoveFromSelection() { if (Tile.Item is { } i) i.IsSelected = false; }
        }
    }
}
