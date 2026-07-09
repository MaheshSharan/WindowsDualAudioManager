using System;
using AudioDual.UI;

namespace AudioDual
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
    }
}
