﻿#light

namespace Vim
open Microsoft.VisualStudio.Text
open Microsoft.VisualStudio.Text.Operations
open Microsoft.VisualStudio.Text.Editor
open System.ComponentModel.Composition
open StringBuilderExtensions
open Vim
open Vim.Interpreter
open System.Text.RegularExpressions

[<Sealed>]
[<Export(typeof<IVimBufferCreationListener>)>]
type internal AutoCommandRunner
    [<ImportingConstructor>]
    (
        _vim : IVim
    ) =

    let _vimData = _vim.VimData
    let _vimHost = _vim.VimHost

    /// Create the Regex for the specified pattern.  The allowed items are specified in ':help autocmd-patterns'
    static let CreateFilePatternRegex (pattern : string) = 
        let builder = System.Text.StringBuilder()
        let mutable i = 0 
        while i < pattern.Length do 
            match pattern.[i] with
            | '*' -> builder.AppendString ".*"
            | '.' -> builder.AppendString "\."
            | '\\' ->
                if i + 1 < pattern.Length then
                    builder.AppendChar pattern.[i + 1]
                    i <- i + 1
                else
                    builder.AppendChar '\\'
            | _ ->
                builder.AppendChar pattern.[i]
            i <- i + 1

        builder.AppendChar '$'

        let bclPattern = builder.ToString()
        VimRegexFactory.CreateBcl bclPattern RegexOptions.None

    static let FileNameEndsWithPattern fileName pattern = 
        try
            let regex = CreateFilePatternRegex pattern
            match regex with 
            | None -> false
            | Some regex -> regex.IsMatch fileName
        with
            _ -> false

    member x.GetAutoCommands fileName eventKind =
        _vimData.AutoCommands
        |> Seq.filter (fun autoCommand -> Seq.exists (fun x -> x = eventKind) autoCommand.EventKinds)
        |> Seq.filter (fun autoCommand -> Seq.exists (FileNameEndsWithPattern fileName) autoCommand.Patterns)
        |> List.ofSeq

    /// Run the specified AutoCommand against the IVimBuffer in question 
    member x.RunAutoCommands (vimBuffer : IVimBuffer) eventKind = 
        let fileName = _vimHost.GetName vimBuffer.TextBuffer
        let autoCommandList = x.GetAutoCommands fileName eventKind
        if autoCommandList.Length > 0 then
            let vimInterpreter = _vim.GetVimInterpreter vimBuffer
            let parser = Parser(_vimData)

            autoCommandList
            |> Seq.iter (fun autoCommand -> 
                match parser.ParseLineCommand autoCommand.LineCommandText with
                | ParseResult.Failed _ -> ()
                | ParseResult.Succeeded lineCommand -> vimInterpreter.RunLineCommand lineCommand |> ignore)

    /// VimBufferCreated is the closest event we have for BufEnter.   
    member x.OnVimBufferCreated (vimBuffer : IVimBuffer) =
        x.RunAutoCommands vimBuffer EventKind.BufEnter

    interface IVimBufferCreationListener with
        member x.VimBufferCreated vimBuffer = x.OnVimBufferCreated vimBuffer

    