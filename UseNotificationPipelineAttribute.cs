// -------------------------------------------------------------------
// Author: Shokhrukhkhon Rustamkhonov
// Date: 25.11.2025
// Description:
// -------------------------------------------------------------------

using System;
using MessagePipe;
using UnityEngine.Scripting;

namespace PipeMediator
{
    [Preserve]
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public class UseNotificationPipelineAttribute : AsyncMessageHandlerFilterAttribute
    {
        public UseNotificationPipelineAttribute(Type type) : base(type) { }

        public UseNotificationPipelineAttribute(Type type, int order) : base(type)
        {
            Order = order;
        }
    }
}