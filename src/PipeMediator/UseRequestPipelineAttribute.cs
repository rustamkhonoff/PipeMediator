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
    public class UseRequestPipelineAttribute : AsyncRequestHandlerFilterAttribute
    {
        public UseRequestPipelineAttribute(Type type) : base(type) { }

        public UseRequestPipelineAttribute(Type type, int order) : base(type)
        {
            Order = order;
        }
    }
}