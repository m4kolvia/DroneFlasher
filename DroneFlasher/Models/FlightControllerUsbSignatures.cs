namespace DroneFlasher.Models
{
    public class FlightControllerUsbSignatures
    {
        public string[] AllowedSignatures { get; set; } =
        [
            "VID_0483&PID_5740",
            "VID_2E3C&PID_5740",
            "VID_28E9&PID_018A",
            "VID_314B&PID_5740",
            "VID_3997&PID_5740",
            "VID_10C4&PID_EA60",
            "VID_1A86&PID_7523",
            "VID_2341"
        ];
    }
}
