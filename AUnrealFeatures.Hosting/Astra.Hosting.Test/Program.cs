namespace AUnrealFeatures.Hosting.Test
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            ModuleInitialization.Initialize();

            new HttpServer();
            new SocketServer();

            await Task.Delay(-1);
        }
    }
}
