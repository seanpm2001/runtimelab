// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//

namespace DefaultNamespace
{
    //@BEGINRENAME; Verify this renames
    //@ENDRENAME; Verify this renames
    using System;

    public class Bug
    {
        public virtual void runTest()
        {
            Decimal dcml1;
            dcml1 = (new Decimal(6) - new Decimal(2)) / new Decimal(4);
            if (dcml1 == 1)
                Console.WriteLine("Test paSsed");
            else
                Console.WriteLine("Test FAiLED");

        }

        public static int Main()
        {
            Bug b = new Bug();
            b.runTest();
            return 100;
        }
    }
    ///// EOF

}
