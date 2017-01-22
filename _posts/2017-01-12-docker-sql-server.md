---
layout: post
title: "SQL Server in Docker Windows Containers"
date: 2017-01-12
comments: false
publish: false
---
### Objective

Notes:
 * Integrated security worked great until I had to connect from outside the container.  Then needed `sa`.
 * How do we handle password for `sa`?  (Normally pass in, but here is internal env.)
 * How to query SQL using osql  (-E for int. sec., -U/P for sa)
 * How to restore schema using SqlPackage
 * Licenses -- be very careful about distributing images for which "you've agreed to license terms"!

### DockerFile

``` powershell
# Microsoft SQL Server 2014 - 12.0.4100.1 (X64)
ADD https://download.microsoft.com/download/1/5/6/156992E6-F7C7-4E55-833D-249BD2348138/ENU/x64/SQLEXPR_x64_ENU.exe /Setup.exe
RUN Write-Host "Installing may take some time..."               ; \
        Start-Process -FilePath '\Setup.exe'                      \
        -ArgumentList '/Q',                                       \
                      '/ACTION=Install',                          \
                      '/FEATURES=SQLEngine',                      \
                      '/INSTANCENAME=MSSQLServer',                \
                      '/TCPENABLED=1',                            \
                      '/SECURITYMODE=SQL',                        \
                      '/SAPWD=docker_12.0.4100.1',                \
                      '/IAcceptSQLServerLicenseTerms'             \
        -Wait                                                   ; \
        net stop mssqlserver                                    ; \
        sc.exe config mssqlserver obj=LocalSystem

# SqlPackage.exe (for applying schema from Visual Studio)
ADD https://download.microsoft.com/download/3/9/1/39135819-06B1-4A07-B9B0-02397E2F5D0F/EN/x64/DacFramework.msi /DacFramework.msi
RUN Start-Process -FilePath 'msiexec.exe'                         \
        -ArgumentList '/quiet','/qn','/norestart',                \
                      '/log','\DacFramework.log',                 \
                      '/i','\DacFramework.msi'                    \
        -Wait
        
# SQL Tools (for querying from the command line, etc.)
RUN SetX /M PATH "\"C:\Program Files\Microsoft SQL Server\120\Tools\Binn;$env:PATH\""
RUN SetX /M PATH "\"C:\Program Files\Microsoft SQL Server\130\DAC\bin;$env:PATH\""

# Connect (allows 2 min for the service to start)
CMD Write-Host \"Starting SQL Server Service:\"                 ; \
        net start mssqlserver                                   ; \
        Write-Host \"Starting osql Query Engine:\"              ; \
        osql -t 120 -U sa -P docker_12.0.4100.1
```        