//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated from a template.
//
//     Manual changes to this file may cause unexpected behavior in your application.
//     Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace IoT
{
    using System;
    using System.Collections.Generic;
    
    public partial class Status
    {
        public int Id { get; set; }
        public int datagramsreceived { get; set; }
        public double ackratio { get; set; }
        public int rxforwarded { get; set; }
        public int datagramssent { get; set; }
        public System.DateTime time { get; set; }
        public double altitude { get; set; }
        public int rxok { get; set; }
        public double latitude { get; set; }
        public string longitude { get; set; }
        public int rxcount { get; set; }
        public int Gateway_Id { get; set; }
    
        public virtual Gateway Gateway { get; set; }
    }
}
