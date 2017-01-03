---
layout: post
title: "Resolving Native Dependencies for RdKafka"
date: 2017-01-04
comments: false
publish: false
---

## Problem

When you run and load code using the RdKafka wrapper, you may encounter the following error:

```
System.TypeInitializationException: The type initializer for 'RdKafka.Internal.LibRdKafka' threw an exception. ---> System.DllNotFoundException: Unable to load DLL 'librdkafka': The specified module could not be found. (Exception from HRESULT: 0x8007007E)
   at RdKafka.Internal.LibRdKafka.NativeMethods.rd_kafka_version()
   at RdKafka.Internal.LibRdKafka..cctor()
   --- End of inner exception stack trace ---
   at RdKafka.Internal.LibRdKafka.conf_new()
   at RdKafka.Internal.SafeConfigHandle.Create()
   at Project.RdKafka.Connect.config(String brokerCsv) in C:\Project\RdKafka.fs:line 165
   at Project.RdKafka.Connect.producer(String source, BrokerCsv _arg1) in C:\Project\RdKafka.fs:line 192
   at Project.RdKafka.subscribeBatch[a](String brokerCsv, String group, String topic, Int32 parallelism, Int32 batchSize, Int32 batchTimeoutMs, FSharpFunc`2 batchHandle) in C:\Project\RdKafka.fs:line 537
   at Project.RdKafka.subscribe(String brokerCsv, String group, String topic, Int32 parallelism, FSharpFunc`2 handle) in C:\Project\RdKafka.fs:line 638
Real: 00:00:02.067, CPU: 00:00:00.765, GC gen0: 5, gen1: 1, gen2: 0
```

## Background

The RdKafka C# wrapper is distributed separately from its C-native dependencies.

The two NuGet packages are:
 * [the C# wrapper](https://www.nuget.org/packages/RdKafka)
 * [the native libraries](https://www.nuget.org/packages/RdKafka.Internal.librdkafka/)

If you're using 0.9.1 or newer, these dependencies are already being copied to your output folder under the `x64` and `x86` directories.  You should see something like the following:

```powershell
 Mode                LastWriteTime         Length Name
----                -------------         ------ ----
-a----        6/27/2016  10:19 PM        2342912 librdkafka.dll
-a----        6/27/2016  10:19 PM          77824 zlib.dll
```

## Causes

1. You are missing the [RdKafka.Internal.librdkafka](https://www.nuget.org/packages/RdKafka.Internal.librdkafka/) library files.  Verify that the `x64` and `x86` folders are included with your installer.

2. If you are using an older version of the C# wrapper library, the native dependencies are not copied from the nuget packages folder.  In this case, you will be missing the `x64` and `x86` folders entirely.  Simply copy these to your output folder with a pre-build event.

   With the [paket](https://fsprojects.github.io/Paket/) dependency manager, your project pre-build event would be:

   ```powershell
     xcopy /y /d /f
        "$(ProjectDir)..\packages\RdKafka.Internal.librdkafka\runtimes\win7-x64\native\*.*"
        "$(TargetDir)"
   ```



3. You are missing the _Visual C++ Redistributable_ package. 64-bit RdKafka 0.9.1 requires [Microsoft Visual C++ 2013 Redistributable (x64) - 12.0.30501](https://www.microsoft.com/en-us/download/details.aspx?id=40784)

   This is problematic because many development workstations include this dependency as part of Visual Studio.  The problem appears during deployment to production.

### Docker for Windows

To resolve (3) above, you can include the C++ redistributable package within your deployment DockerFile.

```powershell
  # Microsoft Visual C++ 2013 Redistributable (x64) - 12.0.30501
  ADD https://download.microsoft.com/download/2/E/6/2E61CFA4-993B-4DD4-91DA-3737CD5CD6E3/vcredist_x64.exe \vcredist_x64.exe
  RUN Start-Process -Wait -FilePath '\vcredist_x64.exe' -ArgumentList '/install /passive /norestart'
  RUN Remove-Item -Force /vcredist_x64.exe
   ```

Assuming Powershell is your default shell:
```powershell
  # Configure Powershell as the Default Shell
  SHELL ["powershell", "-NoProfile", "-Command", "$ErrorActionPreference = 'Stop';"]
```