using System;
using System.Collections;
using System.Collections.Generic;
using System.IO.Ports;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace RfidReaderLib
{
    public class RfidReader
    {
        private SerialPort _serialPort;

        public bool IsOpen => _serialPort?.IsOpen ?? false;

        public void ConfigurePort(string portName, int baudRate = 19200, int dataBits = 8, Parity parity = Parity.None, StopBits stopBits = StopBits.One)
        {
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;
            _serialPort = new SerialPort(portName, baudRate, parity, dataBits, stopBits);
            _serialPort.ReadTimeout = 1000;
            _serialPort.WriteTimeout = 1000;
        }

        public void Open()
        {
            if (_serialPort != null && !_serialPort.IsOpen)
                _serialPort.Open();
        }

        public void Close()
        {
            if (_serialPort != null && _serialPort.IsOpen)
                _serialPort.Close();
        }

        public byte[] ReadHardwareInfo()
        {
            byte[] command = new byte[] { 0xdd, 0x11, 0xef, 0x09, 0x00, 0x00, 0x00 };
            AppendCRC(ref command);
            return SendAndReceive(command);
        }

        public string ParseHardwareInfo(byte[] data)
        {
            if (data.Length < 21) return "資料不足";

            int baseIndex = 4;
            string year = "20" + data[baseIndex + 6].ToString("X2");
            string month = data[baseIndex + 7].ToString("X2");
            int channelCount = data[baseIndex + 8];
            double power = data[baseIndex + 9] / 10.0;
            string hwVer = $"{data[baseIndex + 10] >> 4}.{data[baseIndex + 10] & 0x0F}";
            string fwVer = $"{data[baseIndex + 11] >> 4}.{data[baseIndex + 11] & 0x0F}";
            int serial = (data[baseIndex + 12] << 8) + data[baseIndex + 13];

            return $"製造日期: {year}-{month}, 通道數: {channelCount}, 功率: {power}W, 硬體版本: {hwVer}, 韌體版本: {fwVer}, 序號: {serial}";
        }

        public List<string> ReadMultipleUIDs()
        {
            List<string> uidList = new List<string>();
            byte[] command = new byte[] { 0xDD, 0x11, 0xEF, 0x09, 0x00, 0x01, 0x00, 0x00 };
            AppendCRC(ref command);
            try
            {
                byte[] rawResponse = SendAndReceive(command, (buffer, complete) =>
                {
                    if (buffer.Count > 0)
                    {
                        List<int> temp = FindAllPacketHeaders(buffer.ToArray());
                        for (int i = 0; i < temp.Count; i++)
                        {
                            int start_po = temp[i];
                            int len = 19;
                            byte[] packet = new byte[len];
                            if (start_po + 4 < buffer.Count)
                            {
                                if (buffer[start_po + 3] == 0x0C) complete();
                            }
                        }

                    }

                }, 3000);
        
                List<int> headers = FindAllPacketHeaders(rawResponse);

                for (int i = 0; i < headers.Count; i++)
                {

                    int start_po = headers[i];
                    int len = 19;
                    byte[] packet = new byte[len];
                    if (start_po + len > rawResponse.Length)
                    {
                        len = rawResponse.Length - start_po;
                        packet = new byte[len];
                        Array.Copy(rawResponse, start_po, packet, 0, len);
                        Console.WriteLine($"結尾封包 : {ToHexString(packet)}");
                        continue;
                    }


                    Array.Copy(rawResponse, start_po, packet, 0, len);
                    byte[] uid = new byte[8];
                    Array.Copy(packet, 9, uid, 0, 8);
                    string uidStr = BitConverter.ToString(uid).Replace("-", "");
                    uidList.Add(uidStr);
                }

                Console.WriteLine($"\n✅ 共解析出 {uidList.Count} 張 UID");
                return uidList;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 錯誤: {ex.Message}");
                return uidList;
            }
       
        }
        public class BlockDataResult
        {
            public string UID { get; set; }
            public List<byte[]> Datas { get; set; }
            public byte BlockStatus { get; set; }
        }
        public bool WriteMultipleBlocks(List<string> uidHex, byte blockStart, byte[] blockData)
        {
       
            for(int i = 0; i < uidHex.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(uidHex[i]) || uidHex[i].Length != 16)
                    throw new ArgumentException("UID Hex 字串格式錯誤，必須為 16 字元 (8 bytes)");
                byte[] uid = new byte[8];
                for (int j = 0; j < 8; j++)
                {
                    uid[j] = Convert.ToByte(uidHex[i].Substring(j * 2, 2), 16);
                }
                WriteMultipleBlocks(uid, blockStart, blockData);
            }
            return true;
        }
        public bool WriteMultipleBlocks(string uidHex, byte blockStart, byte[] blockData)
        {
            if (string.IsNullOrWhiteSpace(uidHex) || uidHex.Length != 16)
                throw new ArgumentException("UID Hex 字串格式錯誤，必須為 16 字元 (8 bytes)");

            byte[] uid = new byte[8];
            for (int i = 0; i < 8; i++)
            {
                uid[i] = Convert.ToByte(uidHex.Substring(i * 2, 2), 16);
            }

            return WriteMultipleBlocks(uid, blockStart, blockData);
        }
        public bool WriteMultipleBlocks(byte blockStart, byte[] blockData)
        {
            if (blockData == null || blockData.Length == 0 || blockData.Length % 4 != 0)
                throw new ArgumentException("BlockData 必須為 4 bytes 的倍數");

            byte blockCount = (byte)(blockData.Length / 4);

            List<byte> command = new List<byte>
            {
                0xDD, 0x11, 0xEF,
                (byte)(12 + blockData.Length), // LEN = UID(8) + BlockStart(1) + BlockCount(1) + Data(N)
                0x00,
                0x24,
                0x00,
            };
            command.Add(blockStart);
            command.Add(blockCount);
            command.Add(0x04);
            command.AddRange(blockData);


            byte[] cmdArray = command.ToArray();
            AppendCRC(ref cmdArray);
            Console.WriteLine($"發出命令 : {ToHexString(cmdArray)}");
            byte[] response = SendAndReceive(cmdArray, (buffer, complete) =>
            {
                if (buffer.Count >= 7 && buffer[buffer.Count - 7] == 0x24 && buffer[buffer.Count - 6] == 0x59)
                {
                    complete();
                }
            }, 3000);

            return response.Length > 0;
        }
        public bool WriteMultipleBlocks(byte[] uid, byte blockStart, byte[] blockData)
        {
            if (uid == null || uid.Length != 8)
                throw new ArgumentException("UID 必須是 8 bytes");

            if (blockData == null || blockData.Length == 0 || blockData.Length % 4 != 0)
                throw new ArgumentException("BlockData 必須為 4 bytes 的倍數");

            byte blockCount = (byte)(blockData.Length / 4);

            List<byte> command = new List<byte>
            {
                0xDD, 0x11, 0xEF,
                (byte)(20 + blockData.Length), // LEN = UID(8) + BlockStart(1) + BlockCount(1) + Data(N)
                0x00,
                0x24,
                0x01,              
            };

            byte[] reversedUid = new byte[8];
            Array.Copy(uid, reversedUid, 8);

            command.AddRange(reversedUid);
            command.Add(blockStart);
            command.Add(blockCount);
            command.Add(0x04);
            command.AddRange(blockData);

            byte[] cmdArray = command.ToArray();
            AppendCRC(ref cmdArray);

            byte[] response = SendAndReceive(cmdArray, (buffer, complete) =>
            {
                if (buffer.Count >= 7 && buffer[buffer.Count - 7] == 0x24 && buffer[buffer.Count - 6] == 0x59)
                {
                    complete();
                }
            }, 3000);

            return response.Length > 0;
        }
        public List<BlockDataResult> ReadMultipleBlockData(byte blockStart = 0x00, byte blockCount = 0x05)
        {
            List<BlockDataResult> results = new List<BlockDataResult>();

            byte[] command = new byte[]
            {
                0xDD, 0x11, 0xEF, 0x0B, 0x00, 0x50, 0xFF, blockStart, blockCount
            };
            AppendCRC(ref command);
            string cmd_str = ToHexString(command);
            try
            {
                byte[] response = SendAndReceive(command, (buffer, complete) =>
                {
                    if (buffer.Count >= 7 && buffer[buffer.Count - 7] == 0x50 && buffer[buffer.Count - 6] == 0x59)
                    {
                        complete();
                    }
                }, 3000);

                List<int> headers = FindAllPacketHeaders(response);

                foreach (int start in headers)
                {
                    int len = 19 + 4 * blockCount;
                    if (start + 4 >= response.Length) continue;
                    if (start + len > response.Length) continue;

                    byte[] packet = new byte[len];
                    Array.Copy(response, start, packet, 0, len);

                    if (packet.Length >= 30 && packet[5] == 0x50)
                    {
                        byte blockStatus = packet[6];
                        //if (blockStatus == 0x02)
                        //{
                        //    Console.WriteLine("⚠ 資料錯誤，跳過");
                        //    continue;
                        //}
                        //else if (blockStatus == 0x03)
                        //{
                        //    Console.WriteLine("⚠ 環境干擾過大");
                        //    continue;
                        //}
                        //else if (blockStatus == 0x04)
                        //{
                        //    Console.WriteLine("⚠ 設備異常");
                        //    continue;
                        //}

                        byte[] uid = new byte[8];
                        Array.Copy(packet, 9, uid, 0, 8);
                        string uidStr = BitConverter.ToString(uid).Replace("-", "");
                        for(int i = 0; i < blockCount; i++)
                        {
                            byte[] blockData = new byte[4];
                            Array.Copy(packet, 18 + (i * 4), blockData, 0, 4);
                            Console.WriteLine($"🔹 UID: {uidStr}, Block {i}: {ToHexString(blockData)}");
                        }
                        //int dataLen = len - (13 - 4) - 2;
                        //byte[] data = new byte[dataLen];
                        //Array.Copy(packet, 21, data, 0, dataLen);

                        //results.Add(new BlockDataResult
                        //{
                        //    UID = uidStr,
                        //    Data = data,
                        //    BlockStatus = blockStatus
                        //});
                    }
                }

                Console.WriteLine($"✅ 解析完畢，共 {results.Count} 筆區塊資料");
                return results;
            }
            catch(Exception ex)
            {
                Console.WriteLine($"❌ 錯誤: {ex.Message}");
                return results;
            }
    
           
        }

        private List<int> FindAllPacketHeaders(byte[] data)
        {
            List<int> indexes = new List<int>();

            for (int i = 0; i < data.Length - 2; i++)
            {
                if (data[i] == 0xDD && data[i + 1] == 0x11 && data[i + 2] == 0xEF)
                {
                    indexes.Add(i);
                }
            }

            return indexes;
        }

        private byte[] SendAndReceive(byte[] command, int timeoutMs = 1000, int idleGapMs = 100)
        {
            if (!IsOpen)
                throw new InvalidOperationException("Serial port not open.");

            if (command != null)
            {
                _serialPort.DiscardInBuffer();
                _serialPort.Write(command, 0, command.Length);
            }

            var buffer = new List<byte>();
            int totalWait = 0;
            int idleElapsed = 0;

            while (totalWait < timeoutMs)
            {
                if (_serialPort.BytesToRead > 0)
                {
                    int b = _serialPort.ReadByte();
                    buffer.Add((byte)b);
                    idleElapsed = 0;
                }
                else
                {
                    Thread.Sleep(10);
                    idleElapsed += 10;
                    totalWait += 10;

                    if (idleElapsed >= idleGapMs && buffer.Count > 0)
                        return buffer.ToArray();
                }
            }

            if (buffer.Count > 0)
                return buffer.ToArray();

            throw new TimeoutException("資料接收逾時");
        }
        private byte[] SendAndReceive(byte[] command, Action<List<byte>, Action> checkComplete, int timeoutMs = 1000)
        {
            if (!IsOpen)
                throw new InvalidOperationException("Serial port not open.");

            if (command != null)
            {
                _serialPort.DiscardInBuffer();
                _serialPort.Write(command, 0, command.Length);
            }

            var buffer = new List<byte>();
            bool isDone = false;
            int elapsed = 0;

            Action done = () => { isDone = true; };

            while (elapsed < timeoutMs && !isDone)
            {
                if (_serialPort.BytesToRead > 0)
                {
                    int b = _serialPort.ReadByte();
                    buffer.Add((byte)b);

                    checkComplete(buffer, done);
                }
                else
                {
                    Thread.Sleep(10);
                    elapsed += 10;
                }
            }

            if (isDone)
                return buffer.ToArray();

            throw new TimeoutException("資料接收逾時（未達完成條件）");
        }

        private void AppendCRC(ref byte[] data)
        {
            ushort crc = CalcCRC(data, 0, data.Length);
            Array.Resize(ref data, data.Length + 2);
            data[data.Length - 2] = (byte)(crc & 0xFF);
            data[data.Length - 1] = (byte)((crc >> 8) & 0xFF);
        }

        private ushort CalcCRC(byte[] data, int offset, int length)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < length; i++)
            {
                crc ^= data[offset + i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x0001) != 0)
                        crc = (ushort)((crc >> 1) ^ 0x8408);
                    else
                        crc >>= 1;
                }
            }
            return crc;
        }

        public static string ToHexString(byte[] data) =>
            BitConverter.ToString(data).Replace("-", " ");
    }
}