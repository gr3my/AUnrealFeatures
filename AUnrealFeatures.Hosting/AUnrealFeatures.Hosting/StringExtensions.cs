using System.Security.Cryptography;
using System.Text;

namespace AUnrealFeatures.Hosting
{
    public static class StringExtensions
    {
        private const string Charset = "abcdefghijklmnopqrstuvwxyz0123456789";

        public static string CreateRandomString(int length)
        {
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));

            var result = new StringBuilder(length);
            for (var i = 0; i < length; i++)
                result.Append(Charset[RandomNumberGenerator.GetInt32(Charset.Length)]);
            return result.ToString();
        }
    }
}
