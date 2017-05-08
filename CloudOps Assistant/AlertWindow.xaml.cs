using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace CloudOps_Assistant
{
    /// <summary>
    /// Interaction logic for AlertWindow.xaml
    /// </summary>
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

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }
    }
}
