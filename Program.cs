using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer = System.Timers.Timer;

namespace HDD_Ping
{
  static class Program
  {
    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
      Application.EnableVisualStyles();
      Application.SetCompatibleTextRenderingDefault(false);
      Application.Run(new HDDPing());
    }
  }

  internal class HDDPing : Form
  {
    private NotifyIcon trayIcon;
    private ContextMenu trayMenu;
    private HDDPing_Settings settings;
    private Timer timer;

    public HDDPing()
    {
      trayMenu = new ContextMenu();

      trayIcon = new NotifyIcon();
      trayIcon.Text = "HDD-Ping";
      trayIcon.Icon = Properties.Resources.disabled;

      // Add menu to tray icon and show it.
      trayIcon.ContextMenu = trayMenu;
      trayIcon.Visible = true;

      settings = HDDPing_Settings.GetSettings();
      
      this.CreateTrayOptions();
      trayMenu.MenuItems.Add("-");
      trayMenu.MenuItems.Add("Exit", OnExit);

      this.timer = this.InitializeTimer(settings.Interval);
      this.SetStateIcon();
    }

    protected Timer InitializeTimer(TimeSpan interval)
    {
      var timer = new Timer(interval.TotalMilliseconds);
      timer.Elapsed += (s, e) =>
      {
        this.PingDrives();
      };
      timer.Start();

      return timer;
    }

    private void PingDrives()
    {
      if (settings.DriveSettings.Any(ds => ds.Ping))
      {
        this.SwitchToWorkingIcon();
      }

      foreach (var ds in settings.DriveSettings)
      {
        var guid = Guid.NewGuid().ToString();
        try
        {
          File.WriteAllText(ds.DriveInfo.Name + guid, guid);
          File.Delete(ds.DriveInfo.Name + guid);
        }
        catch { }
      }
    }

    protected void CreateTrayOptions()
    {
      var pingNowItem = new MenuItem("Ping now");
      pingNowItem.Click += (s, e) =>
      {
        this.PingDrives();
      };

      trayMenu.MenuItems.Add(pingNowItem);
      trayMenu.MenuItems.Add("-");

      var drivesMenu = new MenuItem("Ping Drives");
      var drives = DriveInfo.GetDrives();

      foreach (var d in drives)
      {
        try
        {
          if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Network
            && d.DriveType != DriveType.Removable && d.DriveType != DriveType.Ram)
          {
            continue;
          }

          var driveSetting = settings.DriveSettings.SingleOrDefault(setting =>
            setting.DriveInfo.Name == d.Name);
          var mi = new MenuItem(d.Name)
          {
            Checked = driveSetting is DriveSetting && driveSetting.Ping
          };
          mi.Click += (s, e) =>
          {
            mi.Checked = !mi.Checked;
            this.AddOrUpdateDriveSetting(d, mi.Checked);
          };

          drivesMenu.MenuItems.Add(mi);
        }
        catch { }
      }

      trayIcon.ContextMenu.MenuItems.Add(drivesMenu);


      var timerMenu = new MenuItem("Interval");
      var timerItems = new Dictionary<MenuItem, int>();
      foreach (var i in Enumerable.Range(1, 25))
      {
        var tMin = new MenuItem(String.Format("{0} {1}", i, i == 1 ? "Minute" : "Minutes"))
        {
          Checked = (int)settings.Interval.TotalMinutes == i
        };
        tMin.Click += (s, e) =>
        {
          var interval = timerItems[s as MenuItem];
          settings.Interval = TimeSpan.FromMinutes(interval);
          
          this.timer.Enabled = false;
          this.timer.Stop();
          this.timer.Dispose();
          this.InitializeTimer(settings.Interval);

          foreach (var ti in timerItems)
          {
            ti.Key.Checked = false;
          }
          (s as MenuItem).Checked = true;
        };

        timerItems.Add(tMin, i);
        timerMenu.MenuItems.Add(tMin);
      }

      trayIcon.ContextMenu.MenuItems.Add(timerMenu);
    }

    protected void AddOrUpdateDriveSetting(DriveInfo di, bool ping)
    {
      if (!settings.DriveSettings.Any(ds =>
      {
        if (ds.DriveInfo.Name == di.Name)
        {
          ds.Ping = ping;
          return true;
        }

        return false;
      }))
      {
        settings.DriveSettings.Add(new DriveSetting(di, ping));
      }

      this.SetStateIcon();
    }

    protected void SwitchToWorkingIcon()
    {
      trayIcon.Icon = Properties.Resources.working;

      Task.Delay(5000).ContinueWith(finishedTask =>
      {
        this.SetStateIcon();
      });
    }

    protected void SetStateIcon()
    {
      try
      {
        trayIcon.Icon = settings.DriveSettings.Any(ds => ds.Ping) ?
          Properties.Resources.enabled : Properties.Resources.disabled;
      } catch { }
    }

    protected override void OnLoad(EventArgs e)
    {
      Visible = false; // Hide form window.
      ShowInTaskbar = false; // Remove from taskbar.

      base.OnLoad(e);
    }

    private void OnExit(object sender, EventArgs e)
    {
      HDDPing_Settings.TryWriteSettings(settings);

      Application.Exit();
    }

    protected override void Dispose(bool isDisposing)
    {
      if (isDisposing)
      {
        trayIcon.Dispose();
        timer.Dispose();

        if (Properties.Resources.disabled is Icon)
        {
          Properties.Resources.disabled.Dispose();
        }
        if (Properties.Resources.enabled is Icon)
        {
          Properties.Resources.enabled.Dispose();
        }
        if (Properties.Resources.working is Icon)
        {
          Properties.Resources.working.Dispose();
        }
      }

      base.Dispose(isDisposing);
    }
  }

  [Serializable]
  internal class HDDPing_Settings
  {
    public const string ConfigFile = ".settings.ser";

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    public TimeSpan Interval { get; set; }

    public IList<DriveSetting> DriveSettings { get; private set; }

    public HDDPing_Settings(TimeSpan interval, IList<DriveSetting> driveSettings)
    {
      this.Interval = interval;
      this.DriveSettings = driveSettings;
    }

    public static HDDPing_Settings GetSettings()
    {
      if (File.Exists(ConfigFile))
      {
        var bf = new BinaryFormatter();
        using (var stream = File.Open(ConfigFile, FileMode.Open))
        {
          try
          {
            var drives = DriveInfo.GetDrives();
            var setting = bf.Deserialize(stream) as HDDPing_Settings;

            setting.DriveSettings = setting.DriveSettings.Where(ds =>
            {
              return drives.Any(di => di.Name == ds.DriveInfo.Name);
              // filter non-existent drives
            }).ToList();

            return setting;
          }
          catch
          {
            return new HDDPing_Settings(
              HDDPing_Settings.DefaultTimeout, Enumerable.Empty<DriveSetting>().ToList());
          }
        }
      }
      else
      {
        return new HDDPing_Settings(
          HDDPing_Settings.DefaultTimeout, Enumerable.Empty<DriveSetting>().ToList());
      }
    }

    public static bool TryWriteSettings(HDDPing_Settings settings)
    {
      var bf = new BinaryFormatter();
      settings.DriveSettings = settings.DriveSettings.Where(ds => ds.Ping).ToList();
      using (var stream = File.Open(ConfigFile, FileMode.Create))
      {
        try
        {
          bf.Serialize(stream, settings);
          return true;
        }
        catch
        {
          return false;
        }
      }
    }
  }

  [Serializable]
  internal class DriveSetting
  {
    public DriveInfo DriveInfo { get; private set; }
    public bool Ping { get; set; }

    public DriveSetting(DriveInfo di, bool ping)
    {
      this.DriveInfo = di;
      this.Ping = ping;
    }
  }
}
