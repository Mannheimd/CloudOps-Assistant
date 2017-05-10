using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;

namespace CloudOps_Assistant
{
    public partial class AlertWindow : Window
    {
        public AlertWindow(string headerText, string bodyText)
        {
            InitializeComponent();

            alertWindow_NotificationText_TextBlock.Inlines.Add(new Bold(new Run(headerText)));
            alertWindow_NotificationText_TextBlock.Inlines.Add(new LineBreak());
            alertWindow_NotificationText_TextBlock.Inlines.Add(new LineBreak());
            alertWindow_NotificationText_TextBlock.Inlines.Add(new Run(bodyText));
        }

        private void AlertWindow_Dismiss_Button_Click(object sender, RoutedEventArgs e)
        {
            int CurrentX = Convert.ToInt32(GetValue(LeftProperty));
            int CurrentY = Convert.ToInt32(GetValue(TopProperty));

            if (CurrentX != AlertWindowPosition.AlertWindowX || CurrentY != AlertWindowPosition.AlertWindowY)
            {
                MainWindow.SetAlertWindowPosition(CurrentX, CurrentY);
            }

            Close();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            int CurrentX = Convert.ToInt32(GetValue(LeftProperty));
            int CurrentY = Convert.ToInt32(GetValue(TopProperty));

            if (CurrentX != AlertWindowPosition.AlertWindowX || CurrentY != AlertWindowPosition.AlertWindowY)
            {
                MainWindow.SetAlertWindowPosition(CurrentX, CurrentY);
            }
        }

        private void AlertWindow_Deactivated(object sender, EventArgs e)
        {
            this.Topmost = true;
            this.Activate();
        }

        private void AlertWindow_Activated(object sender, EventArgs e)
        {
            this.Topmost = true;
        }
    }
}
