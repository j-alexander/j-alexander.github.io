---
layout: post
title: "Java in Docker Windows Containers"
date: 2017-01-12
comments: false
publish: false
---
### Objective

Notes:
 * Words of [caution](http://blog.takipi.com/running-java-on-docker-youre-breaking-the-law/) regarding JDK & JRE in Docker
   * We're using the JRE, and not distributing it, so "I accept license".
   * How to download a file using .NET in Powershell (w/custom req headers)
 * Used 7-Zip for .tar.gz format.

### DockerFile

``` powershell

# 7 ZIP
ADD http://www.7-zip.org/a/7z1602-x64.exe \7z1602-x64.exe
RUN Start-Process -Wait -FilePath '\7z1602-x64.exe' -ArgumentList '/S'
RUN SetX /M PATH "\"C:\Program Files\7-zip;$env:PATH\""
RUN Remove-Item -Force /7z1602-x64.exe

# Download Java 8 from Oracle
ENV JAVA8_URL http://download.oracle.com/otn-pub/java/jdk/8u102-b14/jre-8u102-windows-x64.tar.gz    
RUN $client = New-Object System.Net.WebClient                                                                 ; \
    $client.Headers.Add([System.Net.HttpRequestHeader]::Cookie, \"oraclelicense=accept-securebackup-cookie\") ; \
    $client.DownloadFile("$env:JAVA8_URL", \"\jre-8u102-windows-x64.tar.gz\")
        
# Install Java 8
RUN 7z.exe e /jre-8u102-windows-x64.tar.gz -o/
RUN 7z.exe x /jre-8u102-windows-x64.tar -o/
RUN SetX /M PATH      "\"\jre1.8.0_102\bin;$env:PATH\""
RUN SetX /M JAVA_HOME "\"\jre1.8.0_102\""

# Java(TM) SE Runtime Environment (build 1.8.0_102-b14)
# Java HotSpot(TM) 64-Bit Server VM (build 25.102-b14, mixed mode)
CMD java -version
```        