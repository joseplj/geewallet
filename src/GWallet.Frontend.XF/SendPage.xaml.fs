﻿namespace GWallet.Frontend.XF

open System
open System.Threading.Tasks

open Xamarin.Forms
open Xamarin.Forms.Xaml

open GWallet.Backend

type CurrencyType =
    Fiat | Crypto

type TransactionInfo =
    { Account: NormalAccount;
      Metadata: IBlockchainFeeInfo;
      Destination: string; 
      Amount: TransferAmount; 
      Passphrase: string; }

type SendPage(account: NormalAccount) =
    inherit ContentPage()
    let _ = base.LoadFromXaml(typeof<SendPage>)

    let GetBalance() =
        let cachedBalance = Caching.RetreiveLastBalance((account:>IAccount).PublicAddress)
        match cachedBalance with
        | NotAvailable -> failwith "Assertion failed: send page should not be accessed if last balance saved on cache was not > 0"
        | Cached(theCachedBalance,_) -> theCachedBalance

    let lastCachedBalance: decimal = GetBalance()

    //FIXME: borrowed this function from Frontend.Console, reuse
    let ShowDecimalForHumans currencyType (amount: decimal): string =
        let amountOfDecimalsToShow =
            match currencyType with
            | CurrencyType.Fiat -> 2
            | CurrencyType.Crypto -> 5

        Math.Round(amount, amountOfDecimalsToShow)

            // line below is to add thousand separators and not show zeroes on the right...
            .ToString("N" + amountOfDecimalsToShow.ToString())


    member private this.ReenableButtons() =
        let mainLayout = base.FindByName<StackLayout>("mainLayout")
        Device.BeginInvokeOnMainThread(fun _ ->
            let sendButton = mainLayout.FindByName<Button>("sendButton")
            sendButton.IsEnabled <- true
            let cancelButton = mainLayout.FindByName<Button>("cancelButton")
            cancelButton.IsEnabled <- true
            sendButton.Text <- "Send"
        )

    member private this.SendTransaction (transactionInfo: TransactionInfo) =
        let maybeTxId =
            try
                Account.SendPayment transactionInfo.Account
                                    transactionInfo.Metadata
                                    transactionInfo.Destination
                                    transactionInfo.Amount
                                    transactionInfo.Passphrase
                                        |> Some
            with
            | :? DestinationEqualToOrigin ->
                let errMsg = "Transaction's origin cannot be the same as the destination."
                Device.BeginInvokeOnMainThread(fun _ ->
                    this.DisplayAlert("Alert", errMsg, "OK").ContinueWith(fun _ ->
                        this.ReenableButtons()
                    ) |> FrontendHelpers.DoubleCheckCompletion
                )
                None
            | :? InsufficientFunds ->
                let errMsg = "Insufficient funds."
                Device.BeginInvokeOnMainThread(fun _ ->
                    this.DisplayAlert("Alert", errMsg, "OK").ContinueWith(fun _ ->
                        this.ReenableButtons()
                    ) |> FrontendHelpers.DoubleCheckCompletion
                )
                None
            | :? InvalidPassword ->
                let errMsg = "Invalid passphrase, try again."
                Device.BeginInvokeOnMainThread(fun _ ->
                    this.DisplayAlert("Alert", errMsg, "OK").ContinueWith(fun _ ->
                        this.ReenableButtons()
                    ) |> FrontendHelpers.DoubleCheckCompletion
                )
                None
        
        match maybeTxId with
        | None -> ()
        | Some(txId) ->
            Device.BeginInvokeOnMainThread(fun _ ->
                this.DisplayAlert("Success", "Transaction sent: " + txId, "OK")
                    .ContinueWith(fun _ ->
                        Device.BeginInvokeOnMainThread(fun _ ->
                            this.Navigation.PopModalAsync() |> FrontendHelpers.DoubleCheckCompletion
                        )
                    ) |> FrontendHelpers.DoubleCheckCompletion
            )
    
    member private this.ValidateAddress currency destinationAddress =
        let inputAddress = destinationAddress
        try
            Account.ValidateAddress currency destinationAddress
            Some(destinationAddress)
        with
        | AddressMissingProperPrefix(possiblePrefixes) ->
            let possiblePrefixesStr = String.Join(", ", possiblePrefixes)
            let msg =  (sprintf "Address starts with the wrong prefix. Valid prefixes: %s."
                                    possiblePrefixesStr)
            this.DisplayAlert("Alert", msg, "OK") |> ignore
            None
        | AddressWithInvalidLength(lengthLimitViolated) ->
            let msg =
                if (inputAddress.Length > lengthLimitViolated) then
                    (sprintf "Address should have a length not higher than %d characters, please try again."
                        lengthLimitViolated)
                else if (inputAddress.Length < lengthLimitViolated) then
                    (sprintf "Address should have a length not lower than %d characters, please try again."
                        lengthLimitViolated)
                else
                    failwith (sprintf "Address introduced '%s' gave a length error with a limit that matches its length: %d=%d"
                                 inputAddress lengthLimitViolated inputAddress.Length)
            this.DisplayAlert("Alert", msg, "OK") |> ignore
            None
        | AddressWithInvalidChecksum(addressWithValidChecksum) ->
            //FIXME: warn user about bad checksum, to see if he wants to continue or not
            // (this text is better borrowed from the Frontend.Console project)
            Some(addressWithValidChecksum)
                
    member this.OnEntryTextChanged(sender: Object, args: EventArgs) =
        let mainLayout = base.FindByName<StackLayout>("mainLayout")
        if (mainLayout = null) then
            //page not yet ready
            ()
        else
            let amountToSend = mainLayout.FindByName<Entry>("amountToSend")
            let passphrase = mainLayout.FindByName<Entry>("passphrase")
            let destinationAddress = mainLayout.FindByName<Entry>("destinationAddress")
            if (destinationAddress = null ||
                passphrase = null ||
                amountToSend = null) then
                ()
            else
                let sendButton = mainLayout.FindByName<Button>("sendButton")
                if (amountToSend.Text <> null && amountToSend.Text.Length > 0) then

                    // FIXME: marking as red should not even mark button as disabled but give the reason in Alert?
                    match Decimal.TryParse(amountToSend.Text) with
                    | false,_ ->
                        amountToSend.TextColor <- Color.Red
                        sendButton.IsEnabled <- false
                    | true,amount ->
                        if (amount <= 0.0m || amount > lastCachedBalance) then
                            amountToSend.TextColor <- Color.Red
                            sendButton.IsEnabled <- false
                        else
                            amountToSend.TextColor <- Color.Default
                            sendButton.IsEnabled <- passphrase.Text <> null && passphrase.Text.Length > 0 &&
                                                    destinationAddress.Text <> null && destinationAddress.Text.Length > 0
                else
                    sendButton.IsEnabled <- false

    member this.OnCancelButtonClicked(sender: Object, args: EventArgs) =
        this.Navigation.PopModalAsync() |> FrontendHelpers.DoubleCheckCompletion

    member private this.DisableButtons() =
        let mainLayout = base.FindByName<StackLayout>("mainLayout")
        let sendButton = mainLayout.FindByName<Button>("sendButton")
        sendButton.IsEnabled <- false
        let cancelButton = mainLayout.FindByName<Button>("cancelButton")
        cancelButton.IsEnabled <- false
        sendButton.Text <- "Sending..."

    member private this.AnswerToFee (txInfo: TransactionInfo) (answer: Task<bool>):unit =
        if (answer.Result) then
            Task.Run(fun _ -> this.SendTransaction txInfo) |> FrontendHelpers.DoubleCheckCompletion
        else
            this.ReenableButtons()

    member this.OnSendButtonClicked(sender: Object, args: EventArgs) =
        let mainLayout = base.FindByName<StackLayout>("mainLayout")
        let amountToSend = mainLayout.FindByName<Entry>("amountToSend")
        let passphrase = mainLayout.FindByName<Entry>("passphrase")
        let destinationAddress = mainLayout.FindByName<Entry>("destinationAddress")

        match Decimal.TryParse(amountToSend.Text) with
        | false,_ ->
            this.DisplayAlert("Alert", "The amount should be a decimal amount", "OK")
                |> FrontendHelpers.DoubleCheckCompletion
        | true,amount ->
            if not (amount > 0.0m) then
                this.DisplayAlert("Alert", "Amount should be positive", "OK") |> FrontendHelpers.DoubleCheckCompletion
            else
                this.DisableButtons()

                let currency = (account:>IAccount).Currency
                let validatedAddress = this.ValidateAddress currency destinationAddress.Text
                match validatedAddress with
                | None -> this.ReenableButtons()
                | Some(destinationAddress) ->

                    let txFeeInfoTask: Task<IBlockchainFeeInfo> = Task.Run(fun _ ->
                        Account.EstimateFee account amount destinationAddress
                    )
                    txFeeInfoTask.ContinueWith(fun (txMetadataWithFeeEstimationTask: Task<IBlockchainFeeInfo>) ->
                        let txMetadataWithFeeEstimation = txMetadataWithFeeEstimationTask.Result
                        let feeAskMsg = sprintf "Estimated fee for this transaction would be: %s %s"
                                              (txMetadataWithFeeEstimation.FeeValue |> ShowDecimalForHumans CurrencyType.Crypto)
                                              (currency.ToString())
                        Device.BeginInvokeOnMainThread(fun _ ->
                            let askFeeTask = this.DisplayAlert("Alert", feeAskMsg, "OK", "Cancel")

                            // FIXME: allow user to specify fiat and/or allbalance
                            let transferAmount = TransferAmount(amount, lastCachedBalance - amount)
                            let txInfo = { Account = account;
                                           Metadata = txMetadataWithFeeEstimation;
                                           Amount = transferAmount;
                                           Destination = destinationAddress;
                                           Passphrase = passphrase.Text; }

                            askFeeTask.ContinueWith(this.AnswerToFee txInfo) |> FrontendHelpers.DoubleCheckCompletion
                        )

                    ) |> FrontendHelpers.DoubleCheckCompletion

