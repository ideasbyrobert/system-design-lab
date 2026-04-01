namespace PaymentSimulator.Api.Simulation;

internal enum PaymentSimulationMode
{
    FastSuccess,
    SlowSuccess,
    Timeout,
    TransientFailure,
    DuplicateCallback,
    DelayedConfirmation
}
