namespace AUnrealFeatures.DependencyInjection.Tests
{
    public static class Program
    {
        static IServiceHost _serviceHost = ServiceHost.Create("Test")
                                                      .AddDependencies(builder =>
                                                      {
                                                          builder.AddSingleton<RandomNumberService, IRandomNumberService>();
                                                          builder.AddSingleton<HelloService, IHelloService>();
                                                      });
        public static void Main(string[] args)
        {
            var serviceProvider = _serviceHost.GetServiceProvider();
            var helloService = serviceProvider.GetService<HelloService>();

            helloService.SayHello();
        }
    }
}
