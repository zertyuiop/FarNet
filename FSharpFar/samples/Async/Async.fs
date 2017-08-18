
(*
    Prototype async helpers for Far jobs, steps, and flows.
*)

module FarNet.Async
open FarNet
open System

[<RequireQualifiedAccess>]
module Job =
    /// Posts Far job.
    let post f =
        far.PostJob (Action f)

    /// Posts Far step.
    let postStep f =
        far.PostStep (Action f)

    /// Posts Far macro.
    let postMacro macro =
        post (fun () -> far.PostMacro macro)

    /// Async.FromContinuations as Far job.
    let fromContinuations f =
        Async.FromContinuations (fun (cont, econt, ccont) ->
            post (fun () -> f (cont, econt, ccont))
        )

    /// Far job from a simple function.
    /// f: Far function with any result.
    let fromFunc f =
        fromContinuations (fun (cont, econt, ccont) ->
            cont (f ())
        )

    /// Far job from a function with await.
    /// f: Far function returning async to await.
    let fromWait f =
        fromContinuations (fun (cont, econt, ccont) ->
            Async.StartWithContinuations (f (), cont, econt, ccont)
        )

    /// Far job from a simple function.
    /// f: Far function with any result.
    let fromFuncStep f =
        let callback (cont, econt, ccont) = cont (f ())
        Async.FromContinuations (fun (cont, econt, ccont) ->
            postStep (fun () -> callback (cont, econt, ccont))
        )

    /// Cancels the flow.
    let cancel : Async<unit> =
        fromContinuations (fun (cont, econt, ccont) ->
            ccont (OperationCanceledException ())
        )

    /// Waits for the predicate is true.
    /// delay: Time to sleep before checks.
    /// sleep: Time to sleep if the predicate is false.
    /// timeout: Maximum waiting time, non positive ~ infinite.
    let await delay sleep timeout predicate = async {
        let timeout = if timeout > 0 then timeout else Int32.MaxValue
        let jobPredicate = fromFunc predicate

        if delay > 0 then
            do! Async.Sleep delay

        let mutable ok = false
        let mutable elapsed = 0
        while not ok && elapsed < timeout do
            let! r = jobPredicate
            ok <- r
            if not ok then
                do! Async.Sleep sleep
                elapsed <- elapsed + sleep

        return ok
    }

    /// Opens the editor and waits for closing.
    let flowEditor (editor: IEditor) = async {
        post (fun () ->
            editor.Open ()
        )
        do! fromFunc (fun () ->
            if not editor.IsOpened then
                invalidOp "Cannot open editor."
        )
        let! _ = Async.AwaitEvent editor.Closed
        ()
    }

    /// Opens the panel and waits for closing.
    let flowPanel (panel: Panel) = async {
        let! _ = await 0 1000 0 (fun () ->
            not far.Window.IsModal
        )
        do! fromFunc (fun () -> 
            if far.Window.Kind <> WindowKind.Panels then
                try
                    far.Window.SetCurrentAt -1
                with exn ->
                    raise (InvalidOperationException ("Cannot set panels.", exn))
        )
        do! fromFuncStep (fun () ->
            panel.Open ()
        )
        do! fromWait (fun () ->
            if far.Panel <> (panel :> IPanel) then
                invalidOp "Cannot open panel."
            async {
                let! _ = Async.AwaitEvent panel.Closed
                ()
            }
        )
    }

    /// Message(text, title) as Far job.
    let message2 text title =
        fromFunc (fun () -> far.Message (text, title))

    /// Message(text, title, options) as Far job.
    let message3 text title options =
        fromFunc (fun () -> far.Message (text, title, options))

    /// Message(text, title, options, buttons) as Far job.
    let message4 text title options buttons =
        fromFunc (fun () -> far.Message (text, title, options, buttons))

/// Posts an exception dialog.
let private postExn exn =
    Job.post (fun () -> far.ShowError (exn.GetType().Name, exn))

module Async =
    /// Starts Far flow with posted errors.
    let farStart flow =
        Async.StartWithContinuations (flow, ignore, postExn, ignore)
