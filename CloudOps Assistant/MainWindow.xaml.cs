using System;
using System.Windows;
using System.Drawing;
using System.Windows.Threading;
using System.Xml;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.IO;
using System.ComponentModel;
using System.Collections.Generic;

namespace CloudOps_Assistant
{
    public partial class MainWindow : Window
    {
        static string InforUrl = @"https://crm.crmcloud.infor.com:443"; // Have this set by a client-side stored value
        static string InforDynamicUrl = null;
        static string InforSystemUrl = null;
        public static Dictionary<string, TimeSpan> TicketRequiredActionTimeDict = new Dictionary<string, TimeSpan>();

        static DispatcherTimer TicketRefreshTimer = new DispatcherTimer();
        public static DateTime LastNotificationTime = new DateTime();
        static TimeSpan ReminderFrequencyTime = new TimeSpan(0, 15, 0);
        static int TicketUpdateErrorCount = 0;
        static int TicketUpdateErrorState = 0;

        static Stream iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/CloudOps Assistant;component/Pelfusion-Long-Shadow-Media-Cloud.ico")).Stream;
        System.Windows.Forms.NotifyIcon ni = new System.Windows.Forms.NotifyIcon()
        {
            Icon = new Icon(iconStream),
            Visible = true
        };

        public MainWindow()
        {
            InitializeComponent();

            ni.DoubleClick +=
                delegate (object sender, EventArgs args)
                {
                    Show();
                    WindowState = WindowState.Normal;
                };

            GetAlertWindowPosition();

            //WindowState = WindowState.Minimized;
            //Hide();

            StartupTasks();
        }

        private async void StartupTasks()
        {
            // Temporary - Add Urgency KVPs into dict
            TicketRequiredActionTimeDict.Add("Low", new TimeSpan(08, 00, 00));
            TicketRequiredActionTimeDict.Add("Medium", new TimeSpan(04, 00, 00));
            TicketRequiredActionTimeDict.Add("High", new TimeSpan(01, 00, 00));
            TicketRequiredActionTimeDict.Add("Critical", new TimeSpan(01, 00, 00));

            // Set Infor URLs
            InforDynamicUrl = InforUrl + @"/sdata/slx/dynamic/-/";
            InforSystemUrl = InforUrl + @"/sdata/slx/system/-/";

            // Populate the ticket status dictionary
            await InforTasks.GetTicketStatuses(InforSystemUrl);

            // Start the clock...
            TicketRefreshTimer.Tick += new EventHandler(TicketRefreshTimer_Tick);
            TicketRefreshTimer.Interval = new TimeSpan(1, 0, 30);
            TicketRefreshTimer.Start();

            // Force an initial ticket update
            await RunTicketUpdate();
        }

        private void Application_Closing(object sender, CancelEventArgs e)
        {
            ni.Dispose();
            ni = null;

            Process.GetCurrentProcess().Kill();
        }

        private async void TicketRefreshTimer_Tick(object sender, EventArgs e)
        {
            await RunTicketUpdate();
        }

        private async Task RunTicketUpdate()
        {
            bool TriggerNewTicketNotification = false;
            bool TriggerTicketReminderNotification = false;
            int HighPriorityTicketCount = 0;
            int CriticalPriorityTicketCount = 0;

            try
            {
                // Get tickets from Infor and update the UI with the ticket list
                CloudOps_TicketList_ListView.ItemsSource = await InforTasks.GetTicketInformation(InforDynamicUrl);

                XmlDocument TicketXml = new XmlDocument();

                XmlNodeList TicketList = TicketXml.GetElementsByTagName("slx:Ticket");
                foreach (XmlNode Node in TicketList)
                {
                    // Get the SData Key
                    string TicketUID = Node.Attributes["sdata:key"].Value;

                    // Using this method to get around the whole XML Schema problem - create a new XmlDocument from the current node
                    XmlDocument TicketNodeDoc = new XmlDocument();
                    TicketNodeDoc.LoadXml(Node.OuterXml);
                    
                    // Get the ticket assigned date
                    XmlNode AssignedDateNode = TicketNodeDoc.GetElementsByTagName("slx:AssignedDate")[0];
                    DateTime AssignedDate = Convert.ToDateTime(AssignedDateNode.InnerText.Trim()).ToUniversalTime();

                    // Get the ticket urgency
                    XmlNode UrgencyNode = TicketNodeDoc.GetElementsByTagName("slx:Description")[0];
                    string Urgency = UrgencyNode.InnerText;

                    // Selecting History node - will pass this for filtering to get the date which the ticket was actually assigned to Ops
                    XmlDocument HistoryNodeDoc = new XmlDocument();
                    HistoryNodeDoc.LoadXml(HistoryNodeDoc.OuterXml);

                    // If ticket is high or critical, assess for a notification
                    if (Urgency == "Critical" || Urgency == "High")
                    {
                        if (LastNotificationTime != new DateTime() && AssignedDate > LastNotificationTime)
                        {
                            TriggerNewTicketNotification = true;
                        }
                        if (Urgency == "Critical")
                        {
                            CriticalPriorityTicketCount++;
                        }
                        else if (Urgency == "High")
                        {
                            HighPriorityTicketCount++;
                        }
                    }
                }

                if ((LastNotificationTime == new DateTime() || (DateTime.UtcNow - LastNotificationTime) > ReminderFrequencyTime)
                    && (HighPriorityTicketCount > 0 || CriticalPriorityTicketCount > 0))
                {
                    TriggerTicketReminderNotification = true;
                }

                if (TriggerNewTicketNotification)
                {
                    string Header = "**NEW** High or Critical ticket(s) in the queue";
                    string Body = String.Format("There are {0} High priority and {1} Critical priority tickets in the queue", HighPriorityTicketCount, CriticalPriorityTicketCount);

                    AlertWindowHandler.OpenAlertWindow(Header, Body);
                }
                else if (TriggerTicketReminderNotification)
                {
                    string Header = "High or Critical ticket reminder";
                    string Body = String.Format("There are {0} High priority and {1} Critical priority tickets in the queue", HighPriorityTicketCount, CriticalPriorityTicketCount);

                    AlertWindowHandler.OpenAlertWindow(Header, Body);
                }

                if (TicketUpdateErrorState == 1)
                {
                    TicketUpdateErrorState = 0;
                    MessageBox.Show("Normal operation has resumed. Notifications will continue to appear as normal.", "CloudOps Assistant Error Resolved");
                }

                TicketUpdateErrorCount = 0;
                TicketUpdateStatus_Label.Content = "Last successful update at " + DateTime.Now.ToLongTimeString();
            }
            catch (Exception error)
            {
                TicketUpdateErrorCount++;
                TicketUpdateStatus_Label.Content = "Update failed " + TicketUpdateErrorCount.ToString() + " time(s)";

                if (TicketUpdateErrorState == 0 && TicketUpdateErrorCount > 10)
                {
                    TicketUpdateErrorState = 1;                
                    MessageBox.Show("Unable to pull ticket information from Infor. Monitor the ticket queue manually, another notification will appear when ticket information is being pulled correctly.", "CloudOps Assistant Error");
                }
            }
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
                Hide();

            base.OnStateChanged(e);
        }

        public static void SetAlertWindowPosition(int X, int Y)
        {
            AlertWindowPosition.AlertWindowX = X;
            AlertWindowPosition.AlertWindowY = Y;

            // Check if registry path exists
            if (CheckOrCreateRegPath())
            {
                // Save encrypted key to registry
                RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Swiftpage Support\CloudOps Assistant", true);
                key.SetValue("AlertWindowPositionX", X);
                key.SetValue("AlertWindowPositionY", Y);
            }
        }

        public static void GetAlertWindowPosition()
        {
            // Check if registry path exists
            if (CheckOrCreateRegPath())
            {
                // Get encrypted key from registry
                try
                {
                    RegistryKey Key = Registry.CurrentUser.OpenSubKey(@"Software\Swiftpage Support\CloudOps Assistant", false);
                    AlertWindowPosition.AlertWindowX = (int)Key.GetValue("AlertWindowPositionX");
                    AlertWindowPosition.AlertWindowY = (int)Key.GetValue("AlertWindowPositionY");
                }
                catch
                {
                    // Do nothing
                }
            }
        }

        public static bool CheckOrCreateRegPath()
        {
            // Check if SubKey HKCU\Software\Swiftpage Support\CloudOps Assistant exists
            RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Swiftpage Support\CloudOps Assistant", false);
            if (key == null)
            {
                // Doesn't exist, let's see if HKCU\Software\Swiftpage Support exists
                key = Registry.CurrentUser.OpenSubKey(@"Software\Swiftpage Support", false);
                if (key == null)
                {
                    // Doesn't exist, try to create 'Swiftpage Support' SubKey
                    key = Registry.CurrentUser.OpenSubKey(@"Software", true);
                    try
                    {
                        key.CreateSubKey("Swiftpage Support");
                    }
                    catch (Exception error)
                    {
                        MessageBox.Show(@"Unable to create SubKey HKCU\Software\Swiftpage Support:\n\n" + error.Message);
                        return false;
                    }
                }

                // 'Swiftpage Support' subkey exists (or has just been created), try creating 'Infor Logins'
                key = Registry.CurrentUser.OpenSubKey(@"Software\Swiftpage Support", true);
                try
                {
                    key.CreateSubKey("CloudOps Assistant");
                }
                catch (Exception error)
                {
                    MessageBox.Show(@"Unable to create SubKey HKCU\Software\Swiftpage Support\CloudOps Assistant:\n\n" + error.Message);
                    return false;
                }
            }
            return true;
        }
    }

    public static class AlertWindowPosition
    {
        private static int _AlertWindowX;
        private static int _AlertWindowY;

        public static int AlertWindowX { get => _AlertWindowX; set => _AlertWindowX = value; }
        public static int AlertWindowY { get => _AlertWindowY; set => _AlertWindowY = value; }
    }

    public class AlertWindowHandler
    {
        private static AlertWindow _AlertWindow = new AlertWindow(null, null);

        public static void OpenAlertWindow(string header, string body)
        {
            if (_AlertWindow.IsInitialized)
            {
                _AlertWindow.Close();
            }

            _AlertWindow = new AlertWindow(header, body)
            {
                Left = AlertWindowPosition.AlertWindowX,
                Top = AlertWindowPosition.AlertWindowY
            };
            _AlertWindow.Show();

            MainWindow.LastNotificationTime = DateTime.UtcNow;
        }
    }

    public class Ticket
    {
        public DateTime CreateDate { get; set; }
        public DateTime AssignedDate { get; set; }
        public DateTime AssignedDateCorrected { get; set; }
        public string StatusCode { get; set; }
        public string Subject { get; set; }
        public string TicketNumber { get; set; }
        public List<TicketHistory> TicketHistoryList = new List<TicketHistory>();
        public string Urgency { get; set; }
        public DateTime DueTime { get; set; }
    }

    public class TicketHistory
    {
        public DateTime CreateDate { get; set; }
        public string FieldName { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
    }
}
