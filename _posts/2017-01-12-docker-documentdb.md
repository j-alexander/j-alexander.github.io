---
layout: post
title: "DocumentDB in Docker Windows Containers"
date: 2017-01-12
comments: false
publish: false
---

### Overview

 * Stunnel
 * Port mapping
 * Certificates

 https://localhost:8081/_explorer/index.html#

options
https://docs.microsoft.com/en-us/azure/documentdb/documentdb-nosql-local-emulator

port mapping
https://technet.microsoft.com/en-us/library/cc731068(v=ws.10).aspx#BKMK_1

fsharp example
https://gist.github.com/jamessdixon/bffa8b1c2c3dc806dc41
from
https://jamessdixon.wordpress.com/2014/12/30/using-documentdb-with-f/

https://docs.microsoft.com/en-us/azure/documentdb/documentdb-nosql-local-emulator-export-ssl-certificates

```fsharp
System.Net.ServicePointManager.ServerCertificateValidationCallback <- new Net.Security.RemoteCertificateValidationCallback(fun _ _ _ _ -> true)
```

```powershell
# FSharp.Core rebinding, and self-signed certificate approval
ADD Scripts/Docker/Environment/Microservices/Fsi.exe.config /Fsi.exe.config
ADD Scripts/Docker/Environment/Microservices/FsiAnyCpu.exe.config /FsiAnyCpu.exe.config

ENV FSIPath "/Program Files (x86)/Microsoft SDKs/F#/4.0/Framework/v4.0"
RUN Get-Content /Fsi.exe.config | Set-Content "$env:FSIPath/Fsi.exe.config"
RUN Get-Content /FsiAnyCpu.exe.config | Set-Content "$env:FSIPath/FsiAnyCpu.exe.config"
```

http://stackoverflow.com/a/27244075/6840746

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.net>
    <settings>
     <servicePointManager
        checkCertificateName="false"
        checkCertificateRevocationList="false" />
    </settings>
  </system.net>
  <runtime>
    <legacyUnhandledExceptionPolicy enabled="true" />
    <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
      <dependentAssembly>
        <assemblyIdentity
          name="FSharp.Core"
          publicKeyToken="b03f5f7f11d50a3a"
          culture="neutral"/>
        <bindingRedirect
          oldVersion="2.0.0.0-4.3.1.0"
          newVersion="4.4.0.0"/>
      </dependentAssembly>
    </assemblyBinding>
  </runtime>
</configuration>
```

`The remote certificate is invalid according to the validation procedure`

```powershell
# Map Ports for DocumentDB
RUN netsh interface portproxy add v4tov4 listenaddress=127.0.0.1 listenport=8080 connectaddress=documentdb connectport=8080 protocol=tcp
RUN netsh interface portproxy add v4tov4 listenaddress=127.0.0.1 listenport=8081 connectaddress=documentdb connectport=28081 protocol=tcp
RUN netsh interface portproxy add v4tov4 listenaddress=127.0.0.1 listenport=10250 connectaddress=documentdb connectport=20250 protocol=tcp
RUN netsh interface portproxy add v4tov4 listenaddress=127.0.0.1 listenport=10251 connectaddress=documentdb connectport=20251 protocol=tcp
RUN netsh interface portproxy add v4tov4 listenaddress=127.0.0.1 listenport=10252 connectaddress=documentdb connectport=20252 protocol=tcp
RUN netsh interface portproxy add v4tov4 listenaddress=127.0.0.1 listenport=10253 connectaddress=documentdb connectport=20253 protocol=tcp
RUN netsh interface portproxy add v4tov4 listenaddress=127.0.0.1 listenport=10254 connectaddress=documentdb connectport=20254 protocol=tcp
```


"`n" | openssl s_client -connect website.com:443 -showcerts > documentdb.cer
cmd /C "echo QUIT`n | openssl s_client -connect localhost:8081 -showcerts > documentdb.cert"

certutil -f -p test -importCert Root documentdb.cer
certutil -f -p test -importCert MY documentdb.cer


certutil -addstore -user -f "My" documentdb.cer
certutil -addstore -user -f "CA" documentdb.cer

keytool -importcert -file blah.crt -alias trustedCertEntry -keystore jre/lib/security/cacerts

### Docker Weirdness

Can you verify that I'm not crazy, when you get a chance?
`docker run --rm -it microsoft/windowsservercore powershell`

Then type dash connect space:
`-connect `

My session disconnects as soon as I hit the space.

[2:01 PM]  
I was trying to run:
`openssl s_client -connect localhost:8081 -showcerts`

It was kicking me off after hitting space between connect and localhost.

https://youtu.be/1-rzKH5CFTk




https://docs.microsoft.com/en-us/azure/documentdb/documentdb-performance-tips







## from before


### documentdb

Great with F#:
* [with fsharp](https://jamessdixon.wordpress.com/2014/12/30/using-documentdb-with-f/)

Designed for localhost only:
* [emulator](https://docs.microsoft.com/en-us/azure/documentdb/documentdb-nosql-local-emulator)

Normally, need to install certificate:
![documentdb certificate 1](documentdb-certificate-1.png)
![documentdb certificate 2](documentdb-certificate-2.png)
![documentdb certificate 3](documentdb-certificate-3.png)

Basically, a _nightmare_ for containerized testing.

Workaround for SSL certificate installation:

* [stunnel](https://www.stunnel.org/downloads.html)

Accept connections on 8080 and route to SSL 8081 using local certificate (`DocumentDB.conf`:

``` ini
engine = capi

[disable-documentdb-ssl]
client = yes
accept = 8080
connect = 127.0.0.1:8081
engineId = capi
```

Account name is the first part of the dns name, and must be `localhost`!

Thus, `docker-compose.yml` service:

``` yaml
  localhost.documentdb:
    build: ..\DockerFiles\DocumentDB
    image: documentdb
    ports:
     - "8080:8080"
     - "8081:8081"
     - "10250:10250"
     - "10251:10251"
     - "10252:10252"
     - "10253:10253"
     - "10254:10254"
    stdin_open: true
```