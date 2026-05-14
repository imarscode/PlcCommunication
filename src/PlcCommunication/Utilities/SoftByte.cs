namespace PlcCommunication.Utilities
{
    /// <summary>位级操作辅助方法。</summary>
    public static class SoftByte
    {
        /// <summary>设置或清除字节中的特定位。</summary>
        public static byte SetBit(byte value, int bit, bool state)
        {
            if (bit < 0 || bit > 7) return value;
            if (state)
                return (byte)(value | (1 << bit));
            else
                return (byte)(value & ~(1 << bit));
        }

        /// <summary>获取特定位的状态。</summary>
        public static bool GetBit(byte value, int bit)
        {
            if (bit < 0 || bit > 7) return false;
            return ((value >> bit) & 0x01) == 0x01;
        }
    }
}
