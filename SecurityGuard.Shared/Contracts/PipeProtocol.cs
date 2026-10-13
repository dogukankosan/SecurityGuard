using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SecurityGuard.Shared.Contracts
{
    /// <summary>
    /// UI - Service pipe mesaj formatı: [4 bayt uzunluk][UTF-8 JSON].
    /// İki taraf da aynı kodu kullanır; format değişirse ikisi birlikte değişir.
    /// </summary>
    public static class PipeProtocol
    {
        /// <summary>Tek mesajın üst sınırı (büyük sorgu sonuçları için geniş tutuldu).</summary>
        public const int MaxMessageBytes = 64 * 1024 * 1024;

        private static readonly ConcurrentDictionary<Type, DataContractJsonSerializer> Serializers =
            new ConcurrentDictionary<Type, DataContractJsonSerializer>();

        public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken token)
        {
            byte[] body = Serialize(message);
            byte[] frame = new byte[4 + body.Length];
            Buffer.BlockCopy(BitConverter.GetBytes(body.Length), 0, frame, 0, 4);
            Buffer.BlockCopy(body, 0, frame, 4, body.Length);

            await stream.WriteAsync(frame, 0, frame.Length, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }

        /// <returns>Mesaj; karşı taraf bağlantıyı düzgün kapattıysa null.</returns>
        public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token) where T : class
        {
            byte[] header = await ReadExactlyAsync(stream, 4, token, true).ConfigureAwait(false);
            if (header == null) return null;

            int length = BitConverter.ToInt32(header, 0);
            if (length < 0 || length > MaxMessageBytes)
                throw new InvalidDataException("Geçersiz mesaj uzunluğu: " + length);

            byte[] body = await ReadExactlyAsync(stream, length, token, false).ConfigureAwait(false);
            return (T)Deserialize(typeof(T), body);
        }

        public static string ToJson(object value)
        {
            return Encoding.UTF8.GetString(Serialize(value));
        }

        public static T FromJson<T>(string json)
        {
            if (string.IsNullOrEmpty(json)) return default(T);
            return (T)Deserialize(typeof(T), Encoding.UTF8.GetBytes(json));
        }

        private static byte[] Serialize(object value)
        {
            using (var ms = new MemoryStream())
            {
                GetSerializer(value.GetType()).WriteObject(ms, value);
                return ms.ToArray();
            }
        }

        private static object Deserialize(Type type, byte[] body)
        {
            using (var ms = new MemoryStream(body))
                return GetSerializer(type).ReadObject(ms);
        }

        private static DataContractJsonSerializer GetSerializer(Type type)
        {
            return Serializers.GetOrAdd(type, t => new DataContractJsonSerializer(t,
                new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }));
        }

        private static async Task<byte[]> ReadExactlyAsync(Stream stream, int count, CancellationToken token, bool allowEof)
        {
            byte[] buffer = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = await stream.ReadAsync(buffer, read, count - read, token).ConfigureAwait(false);
                if (n == 0)
                {
                    if (read == 0 && allowEof) return null;
                    throw new EndOfStreamException("Bağlantı mesajın ortasında kapandı.");
                }
                read += n;
            }
            return buffer;
        }
    }
}
