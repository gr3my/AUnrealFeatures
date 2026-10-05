using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AUnrealFeatures.DependencyInjection.Tests
{
    public interface IHelloService
    {
        void SayHello();    
    }

    public sealed class HelloService : IHelloService
    {
        [Inject] private IRandomNumberService _randomNumberService;

        public void SayHello() 
        {
            Console.WriteLine("HelloService says hello! (" + _randomNumberService.GetRandomNumber() + ")");
        }
    }
}
