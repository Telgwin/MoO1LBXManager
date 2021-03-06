﻿using System;
using System.Collections.Generic;
using System.IO;

namespace LBXManager
{
	internal class LBXFile
	{
		// externalPalette stores the 256-bit color values (3 bytes per color indicating RGB values, arrayed in an lookup index that other files use)
		private static byte[] externalPalette;
		// lookup stores whether or not the parsed byte is actually a readable character
		private static bool[] lookup;

		public LBXFileTypeEnum LBXFileType { get; private set; }

		private int numOfInternalFiles;
		List<InternalFileClass> internalFiles;
		private byte[] bytes;

		public const uint FirstPower = 256;
		public const uint SecondPower = 65536;
		public const uint ThirdPower = 16777216;

		#region Properties
		public List<InternalFileClass> InternalFiles => internalFiles;

		public byte[] Bytes => bytes;

		public static byte[] ExternalPalette => externalPalette;

		public static bool[] Lookup => lookup;
		#endregion

		/// <summary>
		/// Constructor that has sole purpose of making sure the static properties are initialized
		/// </summary>
		public LBXFile()
		{
			if (lookup == null)
			{
				InitializeLookUp();
			}
		}

		/// <summary>
		/// Creates the lookup table used in parsing text from byte values
		/// </summary>
		private static void InitializeLookUp()
		{
			lookup = new bool[65535];
			for (char c = '0'; c <= '9'; c++)
			{
				lookup[c] = true;
			}
			for (char c = 'A'; c <= 'Z'; c++)
			{
				lookup[c] = true;
			}
			for (char c = 'a'; c <= 'z'; c++)
			{
				lookup[c] = true;
			}
			lookup['.'] = true;
			lookup[','] = true;
			lookup[' '] = true;
		}

		/// <summary>
		/// Load in LBX file from file system
		/// </summary>
		/// <param name="fileToLoad">The file to load</param>
		/// <param name="reason">Reason for failure, if any</param>
		/// <returns>True if successful, false otherwise</returns>
		public bool LoadLBXFile(FileInfo fileToLoad, out string reason)
		{
			try
			{
				using (FileStream fileStream = new FileStream(fileToLoad.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					bytes = ReadFile(fileStream);
				}
				if (!ParseMiniHeader())
				{
					reason = "This is not a valid LBX file.";
					return false;
				}
			}
			catch (Exception e)
			{
				reason = string.Format("Failed to load LBX File {0}: " + e.Message, fileToLoad.Name);
				return false;
			}
			reason = null;
			return true;
		}

		/// <summary>
		/// Load in the bytes of the file
		/// </summary>
		/// <param name="stream">The file stream</param>
		/// <returns>The file's bytes</returns>
		private static byte[] ReadFile(Stream stream)
		{
			byte[] buffer = new byte[32768];
			using (MemoryStream ms = new MemoryStream())
			{
				while (true)
				{
					int read = stream.Read(buffer, 0, buffer.Length);
					if (read <= 0)
					{
						return ms.ToArray();
					}
					ms.Write(buffer, 0, read);
				}
			}
		}

		/// <summary>
		/// Function for loading 256-bit color lookup array
		/// </summary>
		/// <param name="fontFile">The file to load from</param>
		public static bool LoadExternalPalette(FileInfo fontFile, out string reason)
		{
			try
			{
				using (FileStream fileStream = new FileStream(fontFile.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					byte[] fontFileBytes = ReadFile(fileStream);
					externalPalette = new byte[768]; // There are 256 colors, and each color have 3 bytes for RGB values (0-255, 0-255, 0-255) so 256*3 = 768
					for (int i = 0; i < externalPalette.Length; i++)
					{
						externalPalette[i] = fontFileBytes[13444 + i];
					}
				}
			}
			catch (Exception e)
			{
				reason = "Failed to load external palette: " + e.Message;
				return false;
			}
			reason = null;
			return true;
		}

		public string GetText()
		{
			if (internalFiles != null && internalFiles.Count > 0)
			{
				return internalFiles[0].GetText(this);
			}
			return string.Empty;
		}

		/// <summary>
		/// Checks if the LBX file has valid markers
		/// </summary>
		/// <returns>True if valid, false otherwise</returns>
		private bool sigCheck()
		{
			if (bytes[2] == 0xAD && bytes[3] == 0xFE && bytes[4] == 0 && bytes[5] == 0)
			{
				return true;
			}
			return false;
		}

		/// <summary>
		/// Parse the internal header for data such as number of internal files, their start and end positions, and other useful data
		/// </summary>
		/// <returns></returns>
		private bool ParseMiniHeader()
		{
			if (bytes == null || bytes.Length < 8)
			{
				return false;
			}
			numOfInternalFiles = (short)(bytes[1] * 256 + bytes[0]);
			if (!sigCheck())
			{
				return false;
			}
			var fileType = (short)(bytes[7] * 256 + bytes[6]);
			switch (fileType)
			{
				case 0: LBXFileType = LBXFileTypeEnum.IMAGE;
					break;
				case 5: LBXFileType = LBXFileTypeEnum.TEXT;
					break;
				default: LBXFileType = LBXFileTypeEnum.UNKNOWN;
					break;
			}
			LoadFiles();
			return true;
		}

		/// <summary>
		/// Loads the internal files' names, start and end positions in byte array
		/// </summary>
		private void LoadFiles()
		{
			internalFiles = new List<InternalFileClass>();
			for (int i = 0; i < numOfInternalFiles; i++)
			{
				InternalFileClass newFile = new InternalFileClass();
				if (LBXFileType == LBXFileTypeEnum.IMAGE)
				{
					char[] name = new char[8];
					char[] comment = new char[24];
					for (int k = 0; k < 8; k++)
					{
						name[k] = lookup[(char)bytes[512 + (i * 32) + k]] ? (char)bytes[512 + (i * 32) + k] : ' ';
					}
					for (int k = 0; k < 24; k++)
					{
						comment[k] = lookup[(char)bytes[520 + (i * 32) + k]] ? (char)bytes[520 + (i * 32) + k] : ' ';
					}
					newFile.Comment = new string(comment);
					newFile.FileName = new string(name);
				}
				else
				{
					newFile.FileName = "File " + (i + 1);
				}
				int currentPos = 8 + (i * 4);
				//Unsigned integers are stored backwards (4 bytes per integer), so read from left to right, while multiplying each byte's value by 256^i, with i starting at 0, to get the real value
				newFile.StartPos = bytes[currentPos + 0] + (uint)(bytes[currentPos + 1] * FirstPower) + (uint)(bytes[currentPos + 2] * SecondPower) +
								   (uint)(bytes[currentPos + 3] * ThirdPower);
				newFile.EndPos = bytes[currentPos + 4] + (uint)(bytes[currentPos + 5] * FirstPower) + (uint)(bytes[currentPos + 6] * SecondPower) +
								   (uint)(bytes[currentPos + 7] * ThirdPower);

				internalFiles.Add(newFile);
			}
		}
	}
}
