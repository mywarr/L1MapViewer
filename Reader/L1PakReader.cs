using System;
using System.Collections.Generic;
using System.IO;
using static L1MapViewer.Other.Struct;

namespace L1MapViewer.Reader {
    class L1PakReader {

        public static byte[]? UnPack(string szIdxType, string szFileName) {

            L1Idx? pIdx = L1IdxReader.Find(szIdxType, szFileName);

            if (pIdx == null) {
                return null;
            }

            byte[] data = Read(pIdx);

            //解DES-通常只有text有加密
            if (pIdx.isDesEncode) {
                data = Algorithm.DecodeDes(data, 0);
            }
            //解壓縮
            if (pIdx.nCompressSize > 0) {
                if (pIdx.nCompressType == 2) {
                    data = Algorithm.BrotliDecompress(data);
                } else if (pIdx.nCompressType == 1) {
                    data = Algorithm.ZilbDecompress(data, pIdx.nSize);
                }
            }

            //解XML
            if (szFileName.ToLower().EndsWith(".spz")) {
                data = DecodeXml(data, 5);
            } else if (szFileName.ToLower().EndsWith(".xml") || szFileName.ToLower().EndsWith(".json") || szFileName.EndsWith(".ui")) {
                data = DecodeXml(data, 4);
            }

            return data;
        }

        //讀取pak內的原檔
        private static byte[] Read(L1Idx pIdx) {

            //有壓縮過的要取壓縮後的長度
            int len = pIdx.nCompressSize > 0 ? pIdx.nCompressSize : pIdx.nSize;

            byte[] data = new byte[len];

            using (FileStream fs = File.Open(pIdx.szPakFullName, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                fs.Seek(pIdx.nPosition, SeekOrigin.Begin);
                fs.Read(data, 0, data.Length);
            }
            return data;
        }

        //AES(xml)
        private static byte[] DecodeXml(byte[] encodeBinary, int headLength) {
            //spz :b[0] = 0x53
            //xml :b[0] = 0x58
            //檔案頭的長度 spz=5 ,xml=4

            List<byte> buffer = new List<byte>();
            try {
                byte[] head = new byte[headLength];
                Array.Copy(encodeBinary, 0, head, 0, head.Length);

                if (head[0] == 0x58) {
                    head[0] = 0x3C; //"X"--> "<"
                }
                buffer.AddRange(head);

                //head是明碼不用解密-->所以分成兩部分處理

                byte[] b = new byte[encodeBinary.Length - headLength];
                Array.Copy(encodeBinary, headLength, b, 0, b.Length);
                buffer.AddRange(Algorithm.DecodeAes(b));
            } catch {
                //
            }
            return buffer.ToArray();
        }
    }
}

