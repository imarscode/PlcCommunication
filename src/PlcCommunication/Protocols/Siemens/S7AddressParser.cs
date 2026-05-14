using System;
using System.Globalization;
using PlcCommunication.Core;

namespace PlcCommunication.Protocols.Siemens
{
    /// <summary>
    /// 将西门子S7地址字符串解析为<see cref="S7AddressData"/>。
    /// 支持以下格式：
    /// <list type="bullet">
    ///   <item><c>DB100.DBW10</c> — DB字</item>
    ///   <item><c>DB100.DBD10</c> — DB双字</item>
    ///   <item><c>DB100.DBB10</c> — DB字节</item>
    ///   <item><c>DB100.DBX10.0</c> — DB位</item>
    ///   <item><c>M100.0</c> — 中间继电器位</item>
    ///   <item><c>MB100</c> — 中间继电器字节</item>
    ///   <item><c>MW100</c> — 中间继电器字</item>
    ///   <item><c>MD100</c> — 中间继电器双字</item>
    ///   <item><c>I0.0</c> / <c>IB0</c> — 输入</item>
    ///   <item><c>Q0.0</c> / <c>QB0</c> — 输出</item>
    ///   <item><c>T0</c> — 定时器</item>
    ///   <item><c>C0</c> — 计数器</item>
    /// </list>
    /// </summary>
    internal static class S7AddressParser
    {
        /// <summary>
        /// 解析S7地址字符串。
        /// </summary>
        /// <exception cref="PlcCommunicationException">当地址格式无效时抛出。</exception>
        public static S7AddressData Parse(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new PlcCommunicationException("S7 address cannot be empty");

            // 移除空白字符
            address = address.Trim().ToUpperInvariant();
            
            if (address.Length == 0)
                throw new PlcCommunicationException("S7 address cannot be empty");

            try
            {
                // DB格式：DB{编号}.{类型}{偏移量}
                if (address.StartsWith("DB", StringComparison.Ordinal))
                {
                    return ParseDBAddress(address);
                }

                // 其他区域：{区域}{类型}{偏移量} 或 {区域}{偏移量}.{位}
                char areaChar = address[0];
                string remainder = address.Substring(1);

                return areaChar switch
                {
                    'M' => ParseMerkerAddress(remainder),
                    'I' => ParseInputAddress(remainder),
                    'Q' => ParseOutputAddress(remainder),
                    'T' => ParseTimerAddress(remainder),
                    'C' => ParseCounterAddress(remainder),
                    _ => throw new PlcCommunicationException($"Unknown S7 area: {areaChar}")
                };
            }
            catch (PlcCommunicationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PlcCommunicationException($"Invalid S7 address format: {address}", ex);
            }
        }

        private static S7AddressData ParseDBAddress(string address)
        {
            // 支持多种格式：
            // 1. DB{编号}.{类型}{偏移量} — 标准格式，如 DB1.DBW0
            // 2. DB{类型}{偏移量} — 简写格式，默认 DB1，如 DBW0
            // 3. DB{编号}.{类型}{偏移量}.{位} — 位访问

            int dotIndex = address.IndexOf('.');
            
            if (dotIndex < 0)
            {
                // 没有点号，可能是简写格式如 DBW0, DBD10
                // 检查是否是 DBX/DBB/DBW/DBD 开头
                if (address.Length > 3)
                {
                    string typePart = address.Substring(2, 1);
                    if (typePart == "X" || typePart == "B" || typePart == "W" || typePart == "D")
                    {
                        // 简写格式，默认 DB1
                        string afterType = address.Substring(3);
                        return ParseAreaAddress(address.Substring(2), S7Area.DataBlocks, 1);
                    }
                }
                throw new PlcCommunicationException($"DB address must include area type: {address}");
            }

            string dbNumberStr = address.Substring(2, dotIndex - 2);
            if (!int.TryParse(dbNumberStr, out int dbNumber))
                throw new PlcCommunicationException($"Invalid DB number: {dbNumberStr}");

            string afterDot = address.Substring(dotIndex + 1);
            return ParseAreaAddress(afterDot, S7Area.DataBlocks, dbNumber);
        }

        private static S7AddressData ParseMerkerAddress(string remainder)
        {
            return ParseAreaAddress(remainder, S7Area.Merkers, 0);
        }

        private static S7AddressData ParseInputAddress(string remainder)
        {
            return ParseAreaAddress(remainder, S7Area.ProcessInputs, 0);
        }

        private static S7AddressData ParseOutputAddress(string remainder)
        {
            return ParseAreaAddress(remainder, S7Area.ProcessOutputs, 0);
        }

        private static S7AddressData ParseTimerAddress(string remainder)
        {
            if (!int.TryParse(remainder, out int offset))
                throw new PlcCommunicationException($"Invalid timer address: T{remainder}");

            return new S7AddressData(S7Area.Timers, 0, offset, 0, S7TransportSize.Timer);
        }

        private static S7AddressData ParseCounterAddress(string remainder)
        {
            if (!int.TryParse(remainder, out int offset))
                throw new PlcCommunicationException($"Invalid counter address: C{remainder}");

            return new S7AddressData(S7Area.Counters, 0, offset, 0, S7TransportSize.Counter);
        }

        private static S7AddressData ParseAreaAddress(string spec, byte area, int dbNumber)
        {
            // spec = {类型}{偏移量} 或 {类型}{偏移量}.{位}
            if (string.IsNullOrEmpty(spec))
                throw new PlcCommunicationException($"Missing address specification for area 0x{area:X2}");

            // DB区域规格有时在类型上有多余的"DB"前缀：例如"DBW10"代替"W10"
            if (area == S7Area.DataBlocks && spec.StartsWith("DB", StringComparison.Ordinal) && spec.Length > 2)
            {
                spec = spec.Substring(2);
            }

            char typeChar = spec[0];

            byte transportSize;
            bool isBitAccess = false;

            string offsetPart = "";
            bool hasTypePrefix = true;
            switch (typeChar)
            {
                case 'X':
                    transportSize = S7TransportSize.Bit;
                    isBitAccess = true;
                    break;
                case 'B':
                    transportSize = S7TransportSize.Byte;
                    break;
                case 'W':
                    transportSize = S7TransportSize.Word;
                    break;
                case 'D':
                    transportSize = S7TransportSize.DWord;
                    break;
                default:
                    hasTypePrefix = false;
                    offsetPart = spec;
                    if (offsetPart.Contains("."))
                    {
                        transportSize = S7TransportSize.Bit;
                        isBitAccess = true;
                    }
                    else if (int.TryParse(spec, out _))
                    {
                        transportSize = S7TransportSize.Byte;
                        int byteOffset = int.Parse(offsetPart);
                        return new S7AddressData(area, dbNumber, byteOffset, 0, transportSize);
                    }
                    else
                    {
                        throw new PlcCommunicationException($"Unknown S7 data type: {typeChar} in {spec}");
                    }
                    break;
            }

            if (hasTypePrefix)
                offsetPart = spec.Substring(1);

            if (isBitAccess && offsetPart.Contains("."))
            {
                string[] parts = offsetPart.Split('.');
                if (parts.Length != 2)
                    throw new PlcCommunicationException($"Invalid bit address format: {spec}");

                int byteOffset = int.Parse(parts[0]);
                byte bitOffset = byte.Parse(parts[1]);

                if (bitOffset > 7)
                    throw new PlcCommunicationException($"Bit offset must be 0-7: {bitOffset}");

                return new S7AddressData(area, dbNumber, byteOffset, bitOffset, transportSize);
            }
            else
            {
                int byteOffset = int.Parse(offsetPart);
                return new S7AddressData(area, dbNumber, byteOffset, 0, transportSize);
            }
        }
    }
}
