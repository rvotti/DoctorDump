using System.Runtime.InteropServices;

Console.WriteLine("SampleCrashingApp");
Console.WriteLine("1. Managed null reference crash");
Console.WriteLine("2. Native access violation crash");
Console.Write("Choose crash type: ");

var choice = Console.ReadLine();

if (choice == "2")
{
    CauseNativeAccessViolation();
}
else
{
    CauseManagedNullReference();
}

static void CauseManagedNullReference()
{
    string? value = null;
    Console.WriteLine(value!.Length);
}

static unsafe void CauseNativeAccessViolation()
{
    Marshal.WriteInt32(IntPtr.Zero, 42);
}

