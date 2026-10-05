using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AUnrealFeatures.DependencyInjection.Tests
{
    public interface IRandomNumberService
    {
        int GetRandomNumber();
    }

    public sealed class RandomNumberService : IRandomNumberService
    {
        public int GetRandomNumber() 
        {
            return Random.Shared.Next(10, 25);
        }
    }
}
