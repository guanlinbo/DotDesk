namespace DotDesk.App
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            using var singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                name: @"Global\DotDesk.App.SingleInstance",
                createdNew: out bool createdNew);

            if (!createdNew)
            {
                MessageBox.Show("DotDesk 已经在运行。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.

            AntdUI.Config.ShowInWindowByMessage = true;
            AntdUI.Config.TextRenderingHighQuality = true;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            ApplicationConfiguration.Initialize();
            Application.Run(new manUi());
        }
    }
}
