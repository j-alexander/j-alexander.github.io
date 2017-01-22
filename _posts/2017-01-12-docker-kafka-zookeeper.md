---
layout: post
title: "Kafka/Zookeeper in Docker Windows Containers"
date: 2017-01-12
comments: false
publish: false
---
### Objective

Notes:
 * 

``` powershell
'#' is not recognized as an internal or external command,
operable program or batch file.
The syntax of the command is incorrect.
```

### DockerFile

``` powershell
FROM java

# Zookeeper from Kafka 0.10.01
ADD http://www-us.apache.org/dist/kafka/0.10.0.1/kafka_2.11-0.10.0.1.tgz /kafka_2.11-0.10.0.1.tgz
RUN powershell -NoProfile -Command \
        7z.exe e \kafka_2.11-0.10.0.1.tgz -o\             ; \
        7z.exe x \kafka_2.11-0.10.0.1.tar -o\             ; \
        mv \kafka_2.11-0.10.0.1 \"\Zookeeper\"            ; \
        rm \kafka_2.11-0.10.0.1.tar                       ; \
        rm \kafka_2.11-0.10.0.1.tgz

# Data Directory
RUN New-Item -Path Data -ItemType Directory
RUN $ZKProps = \"\Zookeeper\config\zookeeper.properties\" ; \
        $ZP = [IO.File]::ReadAllText($ZKProps)            ; \  
        $ZP = $ZP.Replace(\"/tmp/zookeeper\",\"/Data\")   ; \
        [IO.File]::WriteAllText($ZKProps, $ZP)

# Configure Service
ADD server.properties "C:\Kafka\config\server.properties"

CMD Write-Host "Starting Replicator Services (**logs are in /Replicator)"           
# Run Service
CMD /Zookeeper/bin/windows/zookeeper-server-start.bat /Zookeeper/config/

# Run Service
CMD /Kafka/bin/windows/kafka-server-start.bat /Kafka/config/server.propertieszookeeper.properties
```        