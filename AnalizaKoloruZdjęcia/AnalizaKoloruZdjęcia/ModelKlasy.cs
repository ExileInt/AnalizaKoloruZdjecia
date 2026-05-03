using Accord.MachineLearning;
using System;
using System.Collections.Generic;
using System.Text;

namespace AnalizaKoloruZdjęcia
{
    public class ModelKlasy
    {
        public string ClassName { get; set; }

        public GaussianMixtureModel Model { get; set; }
    }
}
