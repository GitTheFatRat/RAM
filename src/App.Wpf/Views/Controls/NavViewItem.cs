namespace RbxManager.App.Views.Controls;

using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using Wpf.Ui.Controls;

public class NavViewItem : NavigationViewItem
{
    static NavViewItem()
    {
        IsActiveProperty.OverrideMetadata(typeof(NavViewItem), new FrameworkPropertyMetadata(false, (d, e) =>
        {
            if (e.NewValue is true && d is NavViewItem item && item.TargetPageType != null)
            {
                MainWindow? mainWindow = Application.Current.MainWindow as MainWindow
                    ?? Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();

                mainWindow?.RootNavigation.Navigate(item.TargetPageType);
            }
        }));
    }

    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new NavViewItemAutomationPeer(this);
    }

    private sealed class NavViewItemAutomationPeer : FrameworkElementAutomationPeer, IInvokeProvider
    {
        private readonly NavViewItem _item;

        public NavViewItemAutomationPeer(NavViewItem item) : base(item)
        {
            _item = item;
        }

        protected override AutomationControlType GetAutomationControlTypeCore()
        {
            return AutomationControlType.Button;
        }

        protected override bool IsControlElementCore() => true;
        protected override bool IsContentElementCore() => true;

        public override object GetPattern(PatternInterface patternInterface)
        {
            if (patternInterface == PatternInterface.Invoke)
            {
                return this;
            }

            return base.GetPattern(patternInterface);
        }

        public void Invoke()
        {
            _item.Dispatcher.Invoke(() =>
            {
                MainWindow? mainWindow = Application.Current.MainWindow as MainWindow
                    ?? Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();

                if (mainWindow != null && _item.TargetPageType != null)
                {
                    mainWindow.RootNavigation.Navigate(_item.TargetPageType);
                }
            });
        }
    }
}
