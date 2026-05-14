using System;

namespace PlcCommunication.Protocols.Modbus
{
    /// <summary>Modbus 功能码常量。</summary>
    public static class ModbusFunction
    {
        public const byte ReadCoils = 0x01;
        public const byte ReadDiscreteInputs = 0x02;
        public const byte ReadHoldingRegisters = 0x03;
        public const byte ReadInputRegisters = 0x04;
        public const byte WriteSingleCoil = 0x05;
        public const byte WriteSingleRegister = 0x06;
        public const byte WriteMultipleCoils = 0x0F;
        public const byte WriteMultipleRegisters = 0x10;
        public const byte MaskWriteRegister = 0x16;
        public const byte ReadWriteMultipleRegisters = 0x17;
    }

    /// <summary>Modbus 的 CRC-16/LRC 计算辅助类。</summary>
    public static class ModbusHelper
    {
        /// <summary>计算给定数据的 CRC-16/MODBUS。</summary>
        public static byte[] CalculateCRC16(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            ushort crc = 0xFFFF;
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x0001) != 0)
                    {
                        crc >>= 1;
                        crc ^= 0xA001;
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }

            // Modbus CRC 为小端序
            return new byte[] { (byte)(crc & 0xFF), (byte)((crc >> 8) & 0xFF) };
        }

        /// <summary>验证 Modbus RTU 消息的 CRC-16。</summary>
        public static bool VerifyCRC16(byte[] message)
        {
            if (message == null || message.Length < 3)
                return false;

            byte[] crc = CalculateCRC16(message, 0, message.Length - 2);
            return crc[0] == message[message.Length - 2] && crc[1] == message[message.Length - 1];
        }

        /// <summary>计算数据子范围的 CRC-16。</summary>
        public static byte[] CalculateCRC16(byte[] data, int offset, int length)
        {
            ushort crc = 0xFFFF;
            int end = offset + length;
            for (int i = offset; i < end; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x0001) != 0)
                    {
                        crc >>= 1;
                        crc ^= 0xA001;
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }
            return new byte[] { (byte)(crc & 0xFF), (byte)((crc >> 8) & 0xFF) };
        }

        /// <summary>计算 Modbus ASCII 的 LRC（纵向冗余校验）。</summary>
        public static byte CalculateLRC(byte[] data)
        {
            if (data == null) return 0;

            byte lrc = 0;
            for (int i = 0; i < data.Length; i++)
            {
                lrc += data[i];
            }
            return (byte)((byte)(-lrc) & 0xFF);
        }

        /// <summary>验证 Modbus ASCII 消息的 LRC。</summary>
        public static bool VerifyLRC(byte[] message)
        {
            if (message == null || message.Length < 2)
                return false;

            byte lrc = CalculateLRC(message, 0, message.Length - 1);
            return lrc == message[message.Length - 1];
        }

        /// <summary>计算数据子范围的 LRC。</summary>
        public static byte CalculateLRC(byte[] data, int offset, int length)
        {
            byte lrc = 0;
            int end = offset + length;
            for (int i = offset; i < end; i++)
            {
                lrc += data[i];
            }
            return (byte)((byte)(-lrc) & 0xFF);
        }

        /// <summary>解析 Modbus 地址字符串。支持十进制和十六进制（0x 前缀）。</summary>
        public static ushort ParseAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentException("Address cannot be empty", nameof(address));

            address = address.Trim();
            
            if (address.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToUInt16(address.Substring(2), 16);

            return ushort.Parse(address);
        }
    }
}
