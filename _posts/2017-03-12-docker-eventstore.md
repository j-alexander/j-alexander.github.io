---
layout: post
title: "EventStore in Docker Windows Containers"
date: 2017-03-12
comments: false
publish: false
---
### Objective

Notes:
 * Simple Service (CMD to start)
 * unpack using Expand-Archive in Powershell
 * IP binding config for eventstore clusternode (otherwise doesn't respond outside of container) 
 * Port-mapping with RUN

### DockerFile

``` powershell
# EventStore 3.8.1
ADD http://download.geteventstore.com/binaries/EventStore-OSS-Win-v3.8.1.zip /EventStore.zip
RUN Expand-Archive -Path /EventStore.zip -DestinationPath /EventStore

# Data Directory
RUN New-Item -Path Data -ItemType Directory
RUN New-Item -Path Logs -ItemType Directory

# Run Service
CMD /EventStore/EventStore.ClusterNode.exe --db /Data --log /Logs --ext-ip 0.0.0.0 --ext-http-prefixes 'http://+:2113/' 
```        