# KoikatuRecorder
 A recorder plugin with multi-thread encoding support.
 
## Feature and Requirement
 Use native multi-thread api to accelerate PNG encoding. The cost of resource is scalable.
 
 Unity 2017 is needed as the least requirement. With the help of class `ImageConversion`, it is possible to deal with GPU synchronization and image encoding separately.
 
## About Compile
 A few dependency from `Bepinex 5` is required, mainly for UI. It is not included in this project. However, if you only need the encoding part, just go ahead.
