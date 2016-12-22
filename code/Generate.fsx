#I "packages/FSharp.Compiler.Service/lib/net45"
#I "packages/FSharpVSPowerTools.Core/lib/net45"
#I "packages/FSharp.Formatting/lib/net40"

#r "FSharp.CodeFormat.dll"
#r "FSharp.Literate.dll"
open FSharp.Literate
open System.IO

let source name =
    Path.Combine(__SOURCE_DIRECTORY__, name)
let target name =
    Path.Combine(__SOURCE_DIRECTORY__, "..\_posts", name)
let generate name =
    let template = source <| sprintf "%s.html" name
    let input = source <| sprintf "%s.fsx" name
    let output = target <| sprintf "%s.html" name
    Literate.ProcessScriptFile(
        input=input,
        output=output,
        templateFile=template,
        format=OutputKind.Html,
        lineNumbers=false)
    
#r "packages/RdKafka/lib/net451/RdKafka.dll"
generate "2016-12-08-rdkafka-for-fsharp-microservices"

#r "packages/FSharp.Data/lib/net40/FSharp.Data.dll"
generate "2016-12-23-jsonpath-queries-using-fsharpdata"
