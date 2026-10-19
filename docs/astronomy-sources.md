# Astronomy data and calculations

Solar events use the existing Open-Meteo provider, with Unix timestamps converted
to the selected city's time zone. Source: https://open-meteo.com/en/docs

Geomagnetic activity uses NOAA SWPC's observed planetary Kp product. Observations
are global, not a local aurora forecast. Values older than six hours are marked
stale. Source: https://www.spaceweather.gov/products/planetary-k-index

The offline moon calculation uses low-order solar/lunar ecliptic coordinates and
the geocentric phase-angle method described in Jean Meeus' Astronomical Algorithms,
chapter 48, also implemented by SunCalc 1.9.0. Illumination and named phases are
approximate; the graphic is a conventional northern-orientation phase symbol,
not the apparent orientation of the moon at the observer's horizon.

Reference: https://github.com/mourner/suncalc/tree/v1.9.0

SunCalc notice for the adapted mathematical calculation:

Copyright (c) 2011-2015, Vladimir Agafonkin

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
