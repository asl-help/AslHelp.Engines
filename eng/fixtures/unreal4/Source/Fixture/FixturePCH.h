//@if <4.15
#pragma once

// Defines the intrinsic that wchar.h of the current universal CRT calls, because these compilers lack it.
// The define sits here because cl doesn't take a function-like macro on its command line.
#define _mm_loadu_si64(p) _mm_loadl_epi64((__m128i const*)(p))

#include "Engine.h"
//@endif
