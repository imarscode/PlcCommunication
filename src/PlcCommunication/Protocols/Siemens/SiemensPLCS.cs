namespace PlcCommunication.Protocols.Siemens
{
    /// <summary>西门子S7 PLC型号枚举。影响PDU大小协商和连接建立。</summary>
    public enum SiemensPLCS
    {
        /// <summary>S7-200（有限的PPI支持，小PDU大小约240字节）</summary>
        S200 = 0,

        /// <summary>S7-300（标准TCP/IP支持）</summary>
        S300 = 10,

        /// <summary>S7-400（高端型号，较大的PDU大小）</summary>
        S400 = 20,

        /// <summary>S7-1200（现代型号，需要PDU协商）</summary>
        S1200 = 30,

        /// <summary>S7-1500（现代型号，大PDU大小）</summary>
        S1500 = 40,
    }
}
