using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace L1MapViewer {
    class Algorithm {

        //DES
        public static byte[] DecodeDes(byte[] src, int index) {
            byte[] destination = new byte[src.Length - index];
            Array.Copy(src, 0 + index, destination, 0, destination.Length);

            if (destination.Length < 0x08) {
                return destination;
            }
#pragma warning disable SYSLIB0021 // DES is obsolete but required for legacy file format compatibility
            using var des = DES.Create();
#pragma warning restore SYSLIB0021
            des.Key = new byte[] { 0x7e, 0x21, 0x40, 0x23, 0x25, 0x5e, 0x24, 0x3c };//~!@#$%^&<
            des.Mode = CipherMode.ECB;
            des.Padding = PaddingMode.None;

            ICryptoTransform ict = des.CreateDecryptor();
            ict.TransformBlock(destination, 0, destination.Length - (destination.Length % 8), destination, 0);
            return destination;
        }

        public static byte[] EncodeDes(byte[] src) {
            byte[] destination = new byte[src.Length];

#pragma warning disable SYSLIB0021 // DES is obsolete but required for legacy file format compatibility
            using var des = DES.Create();
#pragma warning restore SYSLIB0021
            des.Key = new byte[] { 0x7e, 0x21, 0x40, 0x23, 0x25, 0x5e, 0x24, 0x3c };//~!@#$%^&<
            des.Mode = CipherMode.ECB;
            des.Padding = PaddingMode.None;

            ICryptoTransform ict = des.CreateEncryptor();
            int count = ict.TransformBlock(src, 0, src.Length - (src.Length % 8), destination, 0);

            //把餘數補上去
            Array.Copy(src, count, destination, count, (src.Length % 8));
            return destination;
        }


        //AES
        public static byte[] EncodeAes(byte[] _encodeData) {
            int last = _encodeData.Length % 16;
            byte[] lastb = new byte[last];

            //處理餘數
            if (last > 0) {
                byte[] b = new byte[_encodeData.Length - last];
                Array.Copy(_encodeData, b.Length, lastb, 0, last);
                Array.Copy(_encodeData, 0, b, 0, b.Length);
                _encodeData = b;
            }
            //AES...(他AES是CBC加密模式 搭配 NO PADDING)
            using var aes = Aes.Create();
            aes.Padding = PaddingMode.None;

            byte[] key = { 0xDC, 0x84, 0x01, 0x21, 0x2A, 0x40, 0x20, 0x0A, 0xDD, 0x25, 0xB9, 0xA7, 0x0D, 0xB9, 0xC9, 0x4E };
            byte[] iv = { 0x3E, 0x09, 0x78, 0xAA, 0xC4, 0xD5, 0x30, 0x63, 0x30, 0x0C, 0x5F, 0x9A, 0x80, 0x7F, 0x22, 0x46 };

            ICryptoTransform decrypt_AES = aes.CreateEncryptor(key, iv);
            byte[] encodeData = decrypt_AES.TransformFinalBlock(_encodeData, 0, _encodeData.Length);


            byte[] lasta = new byte[last];
            Array.Copy(encodeData, encodeData.Length - 16, lasta, 0, last);

            for (int i = 0; i < last; i++) {
                lastb[i] ^= lasta[i];
            }

            //將本體跟餘數合併
            byte[] result = new byte[encodeData.Length + lastb.Length];
            encodeData.CopyTo(result, 0);
            lastb.CopyTo(result, encodeData.Length);
            return result;
        }


        public static byte[] DecodeAes(byte[] _encodeData) {
            int last = _encodeData.Length % 16;
            byte[] lastb = new byte[last];

            //處理餘數
            if (last > 0) {
                byte[] b = new byte[_encodeData.Length - last];

                Array.Copy(_encodeData, b.Length, lastb, 0, last);
                Array.Copy(_encodeData, 0, b, 0, b.Length);
                _encodeData = b;

                byte[] lasta = new byte[last];
                Array.Copy(_encodeData, _encodeData.Length - 16, lasta, 0, last);

                for (int i = 0; i < last; i++) {
                    lastb[i] ^= lasta[i];
                }
            }
            //AES...(他AES是CBC加密模式 搭配 NO PADDING)
            using var aes = Aes.Create();
            aes.Padding = PaddingMode.None;

            byte[] key = { 0xDC, 0x84, 0x01, 0x21, 0x2A, 0x40, 0x20, 0x0A, 0xDD, 0x25, 0xB9, 0xA7, 0x0D, 0xB9, 0xC9, 0x4E };
            byte[] iv = { 0x3E, 0x09, 0x78, 0xAA, 0xC4, 0xD5, 0x30, 0x63, 0x30, 0x0C, 0x5F, 0x9A, 0x80, 0x7F, 0x22, 0x46 };

            ICryptoTransform decrypt_AES = aes.CreateDecryptor(key, iv);
            byte[] encodeData = decrypt_AES.TransformFinalBlock(_encodeData, 0, _encodeData.Length);

            //將本體跟餘數合併
            byte[] result = new byte[encodeData.Length + lastb.Length];
            encodeData.CopyTo(result, 0);
            lastb.CopyTo(result, encodeData.Length);
            return result;
        }

        //Brotli
        public static byte[] BrotliDecompress(byte[] input) {
            using (MemoryStream msInput = new MemoryStream(input)) {
                using (BrotliStream bs = new BrotliStream(msInput, CompressionMode.Decompress)) {
                    using (MemoryStream msOutput = new MemoryStream()) {
                        bs.CopyTo(msOutput);
                        msOutput.Seek(0, SeekOrigin.Begin);
                        return msOutput.ToArray();
                    }
                }
            }
        }
        public static byte[] BrotliCompress(byte[] input) {
            MemoryStream output = new MemoryStream();
            using (BrotliStream bs = new BrotliStream(output, CompressionMode.Compress)) {
                bs.Write(input, 0, input.Length);
            }
            return output.ToArray();
        }

        //Zilb
        [DllImport("zlib-x64.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int uncompress(byte[] dest, ref uint destLen, byte[] source, uint sourceLen);

        public static byte[] ZilbDecompress(byte[] data, int filesize) {
            byte[] un_buffer = new byte[filesize];// 建立正確檔案大小的數據的緩衝區
            try {
                uint un_len = (uint)filesize;
                int ret = uncompress(un_buffer, ref un_len, data, (uint)data.Length);
            } catch (DllNotFoundException) {
                //
            }
            return un_buffer;
        }
    }
}

