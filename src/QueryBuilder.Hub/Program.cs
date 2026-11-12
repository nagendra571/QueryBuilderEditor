if (args.Length > 0 && args[0] == QueryBuilder.Hub.SignInCodeCommand.Name)
{
    return await QueryBuilder.Hub.SignInCodeCommand.RunAsync(args, Console.Out, Console.Error);
}

QueryBuilder.Hub.HubHost.Build(args).Run();
return 0;
