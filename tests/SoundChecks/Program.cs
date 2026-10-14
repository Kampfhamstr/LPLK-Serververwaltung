int count=0; void Check(bool value,string text){count++;if(!value)throw new Exception(text);}
SoundListeningTests.Run(Check);
SoundNumbersTests.Run(Check);
SoundOverrideTests.Run(Check);
SoundResourcesTests.Run(Check);
SoundRoundLimitTests.Run(Check);
SoundSendTests.Run(Check);
SoundAudienceRuleTests.Run(Check);
WorkshopChecksumTests.Run(Check);
Console.WriteLine($"LPLK Sounds: PASS ({count} regression checks). Client audio not tested.");