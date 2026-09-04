using System.CommandLine;
using Tailor360.Cli.Commands;

var root = new RootCommand(
    "Tailor360 operator command line: migrations, reference data, development seeding, outbox replay " +
    "and feature flags.")
{
    MigrateCommand.Create(),
    InitReferenceDataCommand.Create(),
    SeedSyntheticCommand.Create(),
    ReplayOutboxCommand.Create(),
    FlagsCommand.Create(),
};

return await root.Parse(args).InvokeAsync();
