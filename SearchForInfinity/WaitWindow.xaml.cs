using System.Windows;

namespace SearchForInfinity
{
  public partial class WaitWindow: Window
  {
    public WaitWindow()
    {
      InitializeComponent();
    }

    public static WaitWindow ShowWaitWindow(Window owner, string message = "Please wait...")
    {
      var waitWindow = new WaitWindow
      {
        Owner = owner,
        ShowInTaskbar = false
      };

      // Centrer la fenêtre par rapport à la fenêtre parente
      if (owner != null)
      {
        waitWindow.Left = owner.Left + (owner.Width - waitWindow.Width) / 2;
        waitWindow.Top = owner.Top + (owner.Height - waitWindow.Height) / 2;
      }
      else
      {
        waitWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
      }

      waitWindow.Show();
      waitWindow.UpdateLayout();

      return waitWindow;
    }
  }
}
