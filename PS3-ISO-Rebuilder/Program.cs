using PS3ISORebuilder.IRDFile;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace PS3ISORebuilderConsole
{
    class Program
    {
        private static IRD g_IRD;
        private static string g_root_dir;
        private static string g_outfile;

        private class file_layout_info
        {
            public long start_sector;
            public string path;
            public string md5;

            public file_layout_info(long sector, string file_path, string file_md5)
            {
                start_sector = sector;
                path = file_path;
                md5 = file_md5;
            }
        }

        static int Main(string[] args)
        {
            if (args.Length != 3)
            {
                Console.Error.WriteLine("PS3 ISO Rebuilder: Creates PS3 non-encrypted iso from folder and IRD disc layout description.");
                Console.Error.WriteLine("Usage: PS3-ISO-Rebuilder <path_to_ird> <path_to_jb_folder> <path_to_output_iso>");
                return 1;
            }

            string ird_path = args[0];
            g_root_dir = args[1];
            g_outfile = args[2];

            if (!File.Exists(ird_path))
            {
                Console.Error.WriteLine($"IRD file not found: {ird_path}");
                return 1;
            }

            if (!Directory.Exists(g_root_dir))
            {
                Console.Error.WriteLine($"JB folder not found: {g_root_dir}");
                return 1;
            }

            try
            {
                Console.WriteLine("Loading IRD...");
                g_IRD = new IRD(ird_path);

                if (g_IRD == null || !g_IRD.valid || g_IRD.version < 6)
                {
                    Console.Error.WriteLine("Invalid or unsupported IRD file.");
                    return 1;
                }

                Console.WriteLine($"IRD loaded: Game ID {g_IRD.GAMEID}, Name {g_IRD.GAMENAME}");

                var ird_list = new List<file_layout_info>();

                foreach (var kv in g_IRD.isoheader.filelist)
                {
                    var directory_record = kv.Value;
                    string entry_path = directory_record.entrypath.Replace("\\ ", "\\#");

                    ird_list.Add(new file_layout_info(directory_record.firstDataSector, entry_path, directory_record.md5String));
                }

                Console.WriteLine("Verifying files against IRD...");

                foreach (var item in ird_list.OrderBy(kv => kv.start_sector))
                {
                    string item_full_path = Path.Combine(g_root_dir, item.path.TrimStart('\\', '/'));

                    string ird_MD5 = item.md5;

                    if (!File.Exists(item_full_path))
                    {
                        Console.Error.WriteLine($"Missing: {item.path} MD5: {ird_MD5}");
                        return 1;
                    }

                    string file_MD5 = GetMD5(item_full_path);

                    if (!string.Equals(file_MD5, ird_MD5, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Error.WriteLine($"Wrong CRC: {item.path} (expected {ird_MD5}, got {file_MD5})");
                        return 1;
                    }
                }

                Console.WriteLine("Verification complete.");

                Console.WriteLine("Building ISO (Plain Header)...");

                if (BuildISO(ird_list))
                {
                    Console.WriteLine($"\nISO successfully created: {g_outfile}");
                    return 0;
                }
                else
                {
                    Console.Error.WriteLine("\nISO build failed.");
                    return 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"\nError: {ex.Message}");
                if (ex.StackTrace != null)
                    Console.Error.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        private static bool BuildISO(List<file_layout_info> irdlist)
        {
            try
            {
                // Calculate total size for progress
                long total_bytes = 0;

                foreach (var item_info in irdlist)
                {
                    string item_path = item_info.path;
                    string item_full_path = Path.Combine(g_root_dir, item_path.TrimStart('\\', '/'));

                    if (File.Exists(item_full_path))
                    {
                        total_bytes += new System.IO.FileInfo(item_full_path).Length;
                    }
                    else
                    {
                        Console.Error.WriteLine($"Missing: {item_full_path}");
                        return false;
                    }
                }

                long written_bytes = 0;

                using (var outFileStream = new FileStream(g_outfile, FileMode.Create, FileAccess.Write))
                {
                    // Write header
                    g_IRD.header.Position = 0;
                    g_IRD.header.CopyTo(outFileStream);
                    written_bytes += g_IRD.header.Length; // approximate

                    Console.Write("Progress: 0.0%");

                    // Write files with progress + MD5 verification
                    foreach (var item_info in irdlist.OrderBy(kv => kv.start_sector))
                    {
                        string item_path = item_info.path;
                        string item_full_path = Path.Combine(g_root_dir, item_path.TrimStart('\\', '/'));

                        using (var inputFileStream = File.OpenRead(item_full_path))
                        {
                            outFileStream.Position = item_info.start_sector * g_IRD.isoheader.Blocksize;

                            byte[] buffer = new byte[65536];
                            int bytes_read;

                            using (var md5 = MD5.Create())
                            {
                                while ((bytes_read = inputFileStream.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    outFileStream.Write(buffer, 0, bytes_read);
                                    md5.TransformBlock(buffer, 0, bytes_read, null, 0);
                                    written_bytes += bytes_read;

                                    ShowProgress(written_bytes, total_bytes);
                                }

                                md5.TransformFinalBlock(buffer, 0, 0);
                                string computedMD5 = BitConverter.ToString(md5.Hash).Replace("-", "").ToUpperInvariant();
                                string expectedMD5 = item_info.md5;

                                if (!string.Equals(computedMD5, expectedMD5, StringComparison.OrdinalIgnoreCase))
                                {
                                    Console.Error.WriteLine($"\nCRC mismatch during build for: {item_path}");

                                    if (File.Exists(g_outfile))
                                        File.Delete(g_outfile);

                                    return false;
                                }
                            }
                        }
                    }

                    // Write footer
                    g_IRD.footer.Position = 0;
                    g_IRD.footer.CopyTo(outFileStream);
                }

                // Final progress
                ShowProgress(total_bytes, total_bytes);
                Console.WriteLine(); // New line after progress bar

                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"\nBuild error: {ex.Message}");

                if (File.Exists(g_outfile))
                    File.Delete(g_outfile);

                return false;
            }
        }

        private static void ShowProgress(long current, long total)
        {
            if (total <= 0)
                return;

            float percent = (float)current / total * 100;
            const int BAR_WIDTH = 40;
            int filled = (int)(percent / 100 * BAR_WIDTH);

            string progressBar = $"[{new string('#', filled)}{new string('-', BAR_WIDTH - filled)}] {percent:F1}%";
            Console.Write($"\rProgress: {progressBar}");
        }

        private static string GetMD5(string filePath)
        {
            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
            }
        }

        private static string ToHex(long value)
        {
            return value.ToString("X");
        }
    }
}