using System;
using System.Collections.Generic;
using RfidReaderLib;

class Program
{
    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var reader = new RfidReader();
        reader.ConfigurePort("COM3", 115200);

        try
        {
            reader.Open();
            reader.ReadHardwareInfo();
            Console.WriteLine("✅ 已連接 RFID 讀寫器");
      
            // 目標標籤 UID（16 hex 字元）
            List<string> uids = new List<string>();
            uids = reader.ReadMultipleUIDs();
           
            // 要寫入的資料（2 區塊，每塊 4 bytes，共 8 bytes）
            byte[] data = new byte[]
            {
                0x9A, 0xBC, 0xDE, 0xF0,
                0x12, 0x34, 0x56, 0x78,
         
            };

            // 寫入 block 0 起始
            bool success = reader.WriteMultipleBlocks(uids, 0x00, data);

            reader.ReadBlocksDatas(uids[0], 0, 8);
            Console.WriteLine(success ? "✅ 寫入成功" : "❌ 寫入失敗");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 錯誤: {ex.Message}");
        }
        finally
        {
            reader.Close();
            Console.WriteLine("✔ 已關閉連線");
        }

        Console.WriteLine("按任意鍵結束...");
        Console.ReadKey();
    }
}
