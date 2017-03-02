---
layout: post
title: "Docker Blog Plan"
date: 2017-03-12
comments: false
publish: false
---


nat error https://github.com/docker/docker/issues/27588
install service sometimes doesn't commit


## Docker container setup

[Windows Containers-Quick Start](https://docs.microsoft.com/en-us/virtualization/windowscontainers/quick-start/quick-start-windows-server)

## Docker environment

Installed on the server:

``` powershell
PS C:\> docker --version
Docker version 1.12.2-cs2-ws-beta, build 050b611
```
Adjust the service (using sc in cmd), to start the dockerd.exe as:

``` powershell
"C:\Program Files\docker\dockerd.exe" --run-service -H tcp://0.0.0.0:2375 -H npipe://
```

Installed on the client:

``` powershell
PS C:\> docker --version
Docker version 1.13.0-dev, build d0d0f98

PS C:\> docker-compose --version
docker-compose version 1.8.1, build 004ddae
```

Client environment DOCKER_HOST and PATH settings:

![docker_host environment variable](DOCKER_HOST.png)

![docker path environment variable](docker-PATH.png)


### fsharp
Create an image for F# (based on [Option 3](http://fsharp.org/use/windows/))

Things I learned:

 * How to change the SHELL and how CMD works.
 * Connecting via `docker run` to inspect the state of the machine.
 * `Start-Process -Wait` for many installers (that run async)
   * otherwise they're unpredictable ~ sometimes install ~ sometimes don't
    
``` powershell
FROM microsoft/windowsservercore

# Configure Powershell as the Default Shell
SHELL ["powershell", "-NoProfile", "-Command", "$ErrorActionPreference = 'Stop';"]
```

``` powershell
# .NET 4.5
RUN Install-WindowsFeature Net-Framework-45-Core

# MSBuild Tools     
ADD https://download.microsoft.com/download/E/E/D/EEDF18A8-4AED-4CE0-BEBE-70A83094FC5A/BuildTools_Full.exe \BuildTools_Full.exe
RUN Start-Process -Wait -FilePath '\BuildTools_Full.exe' -ArgumentList '/passive','/norestart'
    
# FSharp 4  
ADD http://download.microsoft.com/download/9/1/2/9122D406-F1E3-4880-A66D-D6C65E8B1545/FSharp_Bundle.exe \FSharp_Bundle.exe
RUN Start-Process -Wait -Filepath '\FSharp_Bundle.exe' -ArgumentList '/install','/quiet'
RUN SetX /M PATH "\"C:\Program Files (x86)\Microsoft SDKs\F#\4.0\Framework\v4.0;$env:PATH\""

# Microsoft (R) F# Interactive version 14.0.23020.0
CMD FsiAnyCpu.exe
```

![docker build fsharp](docker-build-fsharp.gif)


[docker run](https://docs.docker.com/engine/reference/run/)

![docker run fsharp](docker-run-fsharp.gif)

### environment
[docker compose](https://github.com/docker/labs/blob/master/windows/windows-containers/MultiContainerApp.md)

[Fix for DNS](https://twitter.com/friism/status/796139771697315840)
``` powershell
RUN set-itemproperty -path 'HKLM:\SYSTEM\CurrentControlSet\Services\Dnscache\Parameters' -Name ServerPriorityTimeLimit -Value 0 -Type DWord
```

![docker compose up](docker-compose-up.gif)

![docker exec](docker-exec.gif)

![docker-host-task-manager](docker-host-task-manager.png)

Talk about servicepriority app.

![docker compose down](docker-compose-down.gif)

[Ports]

### eventstore

Make sure you accept connections from non-local IPs:

``` powershell
CMD /EventStore/EventStore.ClusterNode.exe --db /Data --log /Logs --ext-ip 0.0.0.0 --ext-http-prefixes 'http://+:2113/' 
```