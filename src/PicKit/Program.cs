namespace PicKit;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new App(args);
        app.InitializeComponent();
        return app.Run();
    }
}
