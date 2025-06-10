using AgGateway.ADAPT.ApplicationDataModel.ADM;

namespace AgGateway.ADAPT.StandardPlugin
{

    internal class Error : IError
    {
        public Error()
        {
            Id = string.Empty;
            Source = string.Empty;
            Description = string.Empty;
            StackTrace = string.Empty;
        }

        public string Id { get; set; }

        public string Source { get; set; }

        public string Description { get; set; }

        public string StackTrace { get; set; }
    }
}
