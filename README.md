### KSIM requires 
* [MS Kinect SDK (v2)](https://www.microsoft.com/en-us/download/details.aspx?id=44561)
* [MS Speech Platform runtime (v11)](https://www.microsoft.com/en-us/download/details.aspx?id=27225)
* MSP acoustic model(s) for English 
    * ["kinect"](https://www.microsoft.com/en-us/download/details.aspx?id=34809) or ["TELE"(telephony)](https://www.microsoft.com/en-us/download/details.aspx?id=27224) 

### Command line options: 
```
Options:
  -l, --listen=VALUE         the microphone to use in speech module("k" to use
                               kinect, edfault: default microphone array).
  -p, --port=VALUE           port number to use to send kinect streams. (
                               default: 8000)
  -g, --grammar=VALUE        grammar file name to use for speech (cfg or grxml,
                               default: out.grxml)
  -h, --help                 show this message
```
