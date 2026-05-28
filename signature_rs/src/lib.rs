use std::os::raw::c_int;

// Prime constants from the C# implementation
const P1: u64 = 0x9E3779B97F4A7C15;
const P2: u64 = 0xC4CEB9FE1A85EC53;
const P3: u64 = 0x165667B19E3779F1;
const P4: u64 = 0x1F79A7AECA2324A5;
const P5: u64 = 0x9616EF3348634979;
const P6: u64 = 0xB8F65595A4934737;
const P7: u64 = 0x0BEB655452634B2B;
const P8: u64 = 0x6295C58D548264A9;
const P9: u64 = 0x11A2968551968C31;
const P10: u64 = 0xEEEF07997F4A7C5B;
const P11: u64 = 0x0CF6FD4E4863490B;

pub const DEFAULT_SEED: u64 = 67890;

#[inline(always)]
fn compute_seeded_hash(seed: u64) -> u64 {
    (P1 ^ seed).wrapping_mul(P2)
}

/// Computes a hash signature for a 3x3 neighborhood
#[inline(always)]
fn compute_hash_vectorized(
    initial: u64,
    tl: u32, tc: u32, tr: u32,
    ml: u32, mc: u32, mr: u32,
    bl: u32, bc: u32, br: u32,
) -> u64 {
    let mut result = initial;

    // Process each value in the 3x3 neighborhood
    result ^= tl as u64; result = result.wrapping_mul(P3);
    result ^= tc as u64; result = result.wrapping_mul(P4);
    result ^= tr as u64; result = result.wrapping_mul(P5);
    result ^= ml as u64; result = result.wrapping_mul(P6);
    result ^= mc as u64; result = result.wrapping_mul(P7);
    result ^= mr as u64; result = result.wrapping_mul(P8);
    result ^= bl as u64; result = result.wrapping_mul(P9);
    result ^= bc as u64; result = result.wrapping_mul(P10);
    result ^= br as u64; result = result.wrapping_mul(P11);

    result
}

#[inline(always)]
unsafe fn compute_hashes_unrolled_4(
    top_row: *const u32,
    middle_row: *const u32,
    bottom_row: *const u32,
    output: *mut u64,
    x: usize,
    initial: u64,
) {
    let t0 = *top_row.add(x);
    let t1 = *top_row.add(x + 1);
    let t2 = *top_row.add(x + 2);
    let t3 = *top_row.add(x + 3);
    let t4 = *top_row.add(x + 4);
    let t5 = *top_row.add(x + 5);

    let m0 = *middle_row.add(x);
    let m1 = *middle_row.add(x + 1);
    let m2 = *middle_row.add(x + 2);
    let m3 = *middle_row.add(x + 3);
    let m4 = *middle_row.add(x + 4);
    let m5 = *middle_row.add(x + 5);

    let b0 = *bottom_row.add(x);
    let b1 = *bottom_row.add(x + 1);
    let b2 = *bottom_row.add(x + 2);
    let b3 = *bottom_row.add(x + 3);
    let b4 = *bottom_row.add(x + 4);
    let b5 = *bottom_row.add(x + 5);

    let mut h0 = initial;
    let mut h1 = initial;
    let mut h2 = initial;
    let mut h3 = initial;

    h0 ^= t0 as u64; h0 = h0.wrapping_mul(P3);
    h1 ^= t1 as u64; h1 = h1.wrapping_mul(P3);
    h2 ^= t2 as u64; h2 = h2.wrapping_mul(P3);
    h3 ^= t3 as u64; h3 = h3.wrapping_mul(P3);

    h0 ^= t1 as u64; h0 = h0.wrapping_mul(P4);
    h1 ^= t2 as u64; h1 = h1.wrapping_mul(P4);
    h2 ^= t3 as u64; h2 = h2.wrapping_mul(P4);
    h3 ^= t4 as u64; h3 = h3.wrapping_mul(P4);

    h0 ^= t2 as u64; h0 = h0.wrapping_mul(P5);
    h1 ^= t3 as u64; h1 = h1.wrapping_mul(P5);
    h2 ^= t4 as u64; h2 = h2.wrapping_mul(P5);
    h3 ^= t5 as u64; h3 = h3.wrapping_mul(P5);

    h0 ^= m0 as u64; h0 = h0.wrapping_mul(P6);
    h1 ^= m1 as u64; h1 = h1.wrapping_mul(P6);
    h2 ^= m2 as u64; h2 = h2.wrapping_mul(P6);
    h3 ^= m3 as u64; h3 = h3.wrapping_mul(P6);

    h0 ^= m1 as u64; h0 = h0.wrapping_mul(P7);
    h1 ^= m2 as u64; h1 = h1.wrapping_mul(P7);
    h2 ^= m3 as u64; h2 = h2.wrapping_mul(P7);
    h3 ^= m4 as u64; h3 = h3.wrapping_mul(P7);

    h0 ^= m2 as u64; h0 = h0.wrapping_mul(P8);
    h1 ^= m3 as u64; h1 = h1.wrapping_mul(P8);
    h2 ^= m4 as u64; h2 = h2.wrapping_mul(P8);
    h3 ^= m5 as u64; h3 = h3.wrapping_mul(P8);

    h0 ^= b0 as u64; h0 = h0.wrapping_mul(P9);
    h1 ^= b1 as u64; h1 = h1.wrapping_mul(P9);
    h2 ^= b2 as u64; h2 = h2.wrapping_mul(P9);
    h3 ^= b3 as u64; h3 = h3.wrapping_mul(P9);

    h0 ^= b1 as u64; h0 = h0.wrapping_mul(P10);
    h1 ^= b2 as u64; h1 = h1.wrapping_mul(P10);
    h2 ^= b3 as u64; h2 = h2.wrapping_mul(P10);
    h3 ^= b4 as u64; h3 = h3.wrapping_mul(P10);

    h0 ^= b2 as u64; h0 = h0.wrapping_mul(P11);
    h1 ^= b3 as u64; h1 = h1.wrapping_mul(P11);
    h2 ^= b4 as u64; h2 = h2.wrapping_mul(P11);
    h3 ^= b5 as u64; h3 = h3.wrapping_mul(P11);

    *output.add(x) = h0;
    *output.add(x + 1) = h1;
    *output.add(x + 2) = h2;
    *output.add(x + 3) = h3;
}

#[inline(always)]
unsafe fn compute_hashes_unrolled_8(
    top_row: *const u32,
    middle_row: *const u32,
    bottom_row: *const u32,
    output: *mut u64,
    x: usize,
    initial: u64,
) {
    let t0 = *top_row.add(x);
    let t1 = *top_row.add(x + 1);
    let t2 = *top_row.add(x + 2);
    let t3 = *top_row.add(x + 3);
    let t4 = *top_row.add(x + 4);
    let t5 = *top_row.add(x + 5);
    let t6 = *top_row.add(x + 6);
    let t7 = *top_row.add(x + 7);
    let t8 = *top_row.add(x + 8);
    let t9 = *top_row.add(x + 9);

    let m0 = *middle_row.add(x);
    let m1 = *middle_row.add(x + 1);
    let m2 = *middle_row.add(x + 2);
    let m3 = *middle_row.add(x + 3);
    let m4 = *middle_row.add(x + 4);
    let m5 = *middle_row.add(x + 5);
    let m6 = *middle_row.add(x + 6);
    let m7 = *middle_row.add(x + 7);
    let m8 = *middle_row.add(x + 8);
    let m9 = *middle_row.add(x + 9);

    let b0 = *bottom_row.add(x);
    let b1 = *bottom_row.add(x + 1);
    let b2 = *bottom_row.add(x + 2);
    let b3 = *bottom_row.add(x + 3);
    let b4 = *bottom_row.add(x + 4);
    let b5 = *bottom_row.add(x + 5);
    let b6 = *bottom_row.add(x + 6);
    let b7 = *bottom_row.add(x + 7);
    let b8 = *bottom_row.add(x + 8);
    let b9 = *bottom_row.add(x + 9);

    let mut h0 = initial;
    let mut h1 = initial;
    let mut h2 = initial;
    let mut h3 = initial;
    let mut h4 = initial;
    let mut h5 = initial;
    let mut h6 = initial;
    let mut h7 = initial;

    h0 ^= t0 as u64; h0 = h0.wrapping_mul(P3);
    h1 ^= t1 as u64; h1 = h1.wrapping_mul(P3);
    h2 ^= t2 as u64; h2 = h2.wrapping_mul(P3);
    h3 ^= t3 as u64; h3 = h3.wrapping_mul(P3);
    h4 ^= t4 as u64; h4 = h4.wrapping_mul(P3);
    h5 ^= t5 as u64; h5 = h5.wrapping_mul(P3);
    h6 ^= t6 as u64; h6 = h6.wrapping_mul(P3);
    h7 ^= t7 as u64; h7 = h7.wrapping_mul(P3);

    h0 ^= t1 as u64; h0 = h0.wrapping_mul(P4);
    h1 ^= t2 as u64; h1 = h1.wrapping_mul(P4);
    h2 ^= t3 as u64; h2 = h2.wrapping_mul(P4);
    h3 ^= t4 as u64; h3 = h3.wrapping_mul(P4);
    h4 ^= t5 as u64; h4 = h4.wrapping_mul(P4);
    h5 ^= t6 as u64; h5 = h5.wrapping_mul(P4);
    h6 ^= t7 as u64; h6 = h6.wrapping_mul(P4);
    h7 ^= t8 as u64; h7 = h7.wrapping_mul(P4);

    h0 ^= t2 as u64; h0 = h0.wrapping_mul(P5);
    h1 ^= t3 as u64; h1 = h1.wrapping_mul(P5);
    h2 ^= t4 as u64; h2 = h2.wrapping_mul(P5);
    h3 ^= t5 as u64; h3 = h3.wrapping_mul(P5);
    h4 ^= t6 as u64; h4 = h4.wrapping_mul(P5);
    h5 ^= t7 as u64; h5 = h5.wrapping_mul(P5);
    h6 ^= t8 as u64; h6 = h6.wrapping_mul(P5);
    h7 ^= t9 as u64; h7 = h7.wrapping_mul(P5);

    h0 ^= m0 as u64; h0 = h0.wrapping_mul(P6);
    h1 ^= m1 as u64; h1 = h1.wrapping_mul(P6);
    h2 ^= m2 as u64; h2 = h2.wrapping_mul(P6);
    h3 ^= m3 as u64; h3 = h3.wrapping_mul(P6);
    h4 ^= m4 as u64; h4 = h4.wrapping_mul(P6);
    h5 ^= m5 as u64; h5 = h5.wrapping_mul(P6);
    h6 ^= m6 as u64; h6 = h6.wrapping_mul(P6);
    h7 ^= m7 as u64; h7 = h7.wrapping_mul(P6);

    h0 ^= m1 as u64; h0 = h0.wrapping_mul(P7);
    h1 ^= m2 as u64; h1 = h1.wrapping_mul(P7);
    h2 ^= m3 as u64; h2 = h2.wrapping_mul(P7);
    h3 ^= m4 as u64; h3 = h3.wrapping_mul(P7);
    h4 ^= m5 as u64; h4 = h4.wrapping_mul(P7);
    h5 ^= m6 as u64; h5 = h5.wrapping_mul(P7);
    h6 ^= m7 as u64; h6 = h6.wrapping_mul(P7);
    h7 ^= m8 as u64; h7 = h7.wrapping_mul(P7);

    h0 ^= m2 as u64; h0 = h0.wrapping_mul(P8);
    h1 ^= m3 as u64; h1 = h1.wrapping_mul(P8);
    h2 ^= m4 as u64; h2 = h2.wrapping_mul(P8);
    h3 ^= m5 as u64; h3 = h3.wrapping_mul(P8);
    h4 ^= m6 as u64; h4 = h4.wrapping_mul(P8);
    h5 ^= m7 as u64; h5 = h5.wrapping_mul(P8);
    h6 ^= m8 as u64; h6 = h6.wrapping_mul(P8);
    h7 ^= m9 as u64; h7 = h7.wrapping_mul(P8);

    h0 ^= b0 as u64; h0 = h0.wrapping_mul(P9);
    h1 ^= b1 as u64; h1 = h1.wrapping_mul(P9);
    h2 ^= b2 as u64; h2 = h2.wrapping_mul(P9);
    h3 ^= b3 as u64; h3 = h3.wrapping_mul(P9);
    h4 ^= b4 as u64; h4 = h4.wrapping_mul(P9);
    h5 ^= b5 as u64; h5 = h5.wrapping_mul(P9);
    h6 ^= b6 as u64; h6 = h6.wrapping_mul(P9);
    h7 ^= b7 as u64; h7 = h7.wrapping_mul(P9);

    h0 ^= b1 as u64; h0 = h0.wrapping_mul(P10);
    h1 ^= b2 as u64; h1 = h1.wrapping_mul(P10);
    h2 ^= b3 as u64; h2 = h2.wrapping_mul(P10);
    h3 ^= b4 as u64; h3 = h3.wrapping_mul(P10);
    h4 ^= b5 as u64; h4 = h4.wrapping_mul(P10);
    h5 ^= b6 as u64; h5 = h5.wrapping_mul(P10);
    h6 ^= b7 as u64; h6 = h6.wrapping_mul(P10);
    h7 ^= b8 as u64; h7 = h7.wrapping_mul(P10);

    h0 ^= b2 as u64; h0 = h0.wrapping_mul(P11);
    h1 ^= b3 as u64; h1 = h1.wrapping_mul(P11);
    h2 ^= b4 as u64; h2 = h2.wrapping_mul(P11);
    h3 ^= b5 as u64; h3 = h3.wrapping_mul(P11);
    h4 ^= b6 as u64; h4 = h4.wrapping_mul(P11);
    h5 ^= b7 as u64; h5 = h5.wrapping_mul(P11);
    h6 ^= b8 as u64; h6 = h6.wrapping_mul(P11);
    h7 ^= b9 as u64; h7 = h7.wrapping_mul(P11);

    *output.add(x) = h0;
    *output.add(x + 1) = h1;
    *output.add(x + 2) = h2;
    *output.add(x + 3) = h3;
    *output.add(x + 4) = h4;
    *output.add(x + 5) = h5;
    *output.add(x + 6) = h6;
    *output.add(x + 7) = h7;
}

#[inline(always)]
unsafe fn compute_hashes_unrolled_6(
    top_row: *const u32,
    middle_row: *const u32,
    bottom_row: *const u32,
    output: *mut u64,
    x: usize,
    initial: u64,
) {
    let t0 = *top_row.add(x);
    let t1 = *top_row.add(x + 1);
    let t2 = *top_row.add(x + 2);
    let t3 = *top_row.add(x + 3);
    let t4 = *top_row.add(x + 4);
    let t5 = *top_row.add(x + 5);
    let t6 = *top_row.add(x + 6);
    let t7 = *top_row.add(x + 7);

    let m0 = *middle_row.add(x);
    let m1 = *middle_row.add(x + 1);
    let m2 = *middle_row.add(x + 2);
    let m3 = *middle_row.add(x + 3);
    let m4 = *middle_row.add(x + 4);
    let m5 = *middle_row.add(x + 5);
    let m6 = *middle_row.add(x + 6);
    let m7 = *middle_row.add(x + 7);

    let b0 = *bottom_row.add(x);
    let b1 = *bottom_row.add(x + 1);
    let b2 = *bottom_row.add(x + 2);
    let b3 = *bottom_row.add(x + 3);
    let b4 = *bottom_row.add(x + 4);
    let b5 = *bottom_row.add(x + 5);
    let b6 = *bottom_row.add(x + 6);
    let b7 = *bottom_row.add(x + 7);

    let mut h0 = initial;
    let mut h1 = initial;
    let mut h2 = initial;
    let mut h3 = initial;
    let mut h4 = initial;
    let mut h5 = initial;

    h0 ^= t0 as u64; h0 = h0.wrapping_mul(P3);
    h1 ^= t1 as u64; h1 = h1.wrapping_mul(P3);
    h2 ^= t2 as u64; h2 = h2.wrapping_mul(P3);
    h3 ^= t3 as u64; h3 = h3.wrapping_mul(P3);
    h4 ^= t4 as u64; h4 = h4.wrapping_mul(P3);
    h5 ^= t5 as u64; h5 = h5.wrapping_mul(P3);

    h0 ^= t1 as u64; h0 = h0.wrapping_mul(P4);
    h1 ^= t2 as u64; h1 = h1.wrapping_mul(P4);
    h2 ^= t3 as u64; h2 = h2.wrapping_mul(P4);
    h3 ^= t4 as u64; h3 = h3.wrapping_mul(P4);
    h4 ^= t5 as u64; h4 = h4.wrapping_mul(P4);
    h5 ^= t6 as u64; h5 = h5.wrapping_mul(P4);

    h0 ^= t2 as u64; h0 = h0.wrapping_mul(P5);
    h1 ^= t3 as u64; h1 = h1.wrapping_mul(P5);
    h2 ^= t4 as u64; h2 = h2.wrapping_mul(P5);
    h3 ^= t5 as u64; h3 = h3.wrapping_mul(P5);
    h4 ^= t6 as u64; h4 = h4.wrapping_mul(P5);
    h5 ^= t7 as u64; h5 = h5.wrapping_mul(P5);

    h0 ^= m0 as u64; h0 = h0.wrapping_mul(P6);
    h1 ^= m1 as u64; h1 = h1.wrapping_mul(P6);
    h2 ^= m2 as u64; h2 = h2.wrapping_mul(P6);
    h3 ^= m3 as u64; h3 = h3.wrapping_mul(P6);
    h4 ^= m4 as u64; h4 = h4.wrapping_mul(P6);
    h5 ^= m5 as u64; h5 = h5.wrapping_mul(P6);

    h0 ^= m1 as u64; h0 = h0.wrapping_mul(P7);
    h1 ^= m2 as u64; h1 = h1.wrapping_mul(P7);
    h2 ^= m3 as u64; h2 = h2.wrapping_mul(P7);
    h3 ^= m4 as u64; h3 = h3.wrapping_mul(P7);
    h4 ^= m5 as u64; h4 = h4.wrapping_mul(P7);
    h5 ^= m6 as u64; h5 = h5.wrapping_mul(P7);

    h0 ^= m2 as u64; h0 = h0.wrapping_mul(P8);
    h1 ^= m3 as u64; h1 = h1.wrapping_mul(P8);
    h2 ^= m4 as u64; h2 = h2.wrapping_mul(P8);
    h3 ^= m5 as u64; h3 = h3.wrapping_mul(P8);
    h4 ^= m6 as u64; h4 = h4.wrapping_mul(P8);
    h5 ^= m7 as u64; h5 = h5.wrapping_mul(P8);

    h0 ^= b0 as u64; h0 = h0.wrapping_mul(P9);
    h1 ^= b1 as u64; h1 = h1.wrapping_mul(P9);
    h2 ^= b2 as u64; h2 = h2.wrapping_mul(P9);
    h3 ^= b3 as u64; h3 = h3.wrapping_mul(P9);
    h4 ^= b4 as u64; h4 = h4.wrapping_mul(P9);
    h5 ^= b5 as u64; h5 = h5.wrapping_mul(P9);

    h0 ^= b1 as u64; h0 = h0.wrapping_mul(P10);
    h1 ^= b2 as u64; h1 = h1.wrapping_mul(P10);
    h2 ^= b3 as u64; h2 = h2.wrapping_mul(P10);
    h3 ^= b4 as u64; h3 = h3.wrapping_mul(P10);
    h4 ^= b5 as u64; h4 = h4.wrapping_mul(P10);
    h5 ^= b6 as u64; h5 = h5.wrapping_mul(P10);

    h0 ^= b2 as u64; h0 = h0.wrapping_mul(P11);
    h1 ^= b3 as u64; h1 = h1.wrapping_mul(P11);
    h2 ^= b4 as u64; h2 = h2.wrapping_mul(P11);
    h3 ^= b5 as u64; h3 = h3.wrapping_mul(P11);
    h4 ^= b6 as u64; h4 = h4.wrapping_mul(P11);
    h5 ^= b7 as u64; h5 = h5.wrapping_mul(P11);

    *output.add(x) = h0;
    *output.add(x + 1) = h1;
    *output.add(x + 2) = h2;
    *output.add(x + 3) = h3;
    *output.add(x + 4) = h4;
    *output.add(x + 5) = h5;
}

#[inline(never)]
unsafe fn compute_signatures_64x64(source: *const u32, destination: *mut u64, initial: u64) {
    const WIDTH: usize = 64;
    const INTERIOR_WIDTH: usize = WIDTH - 2;

    for y in 1..(WIDTH - 1) {
        let top_row = source.add((y - 1) * WIDTH);
        let middle_row = top_row.add(WIDTH);
        let bottom_row = middle_row.add(WIDTH);
        let output = destination.add(y * WIDTH + 1);
        let mut x = 0usize;

        while x + 8 <= INTERIOR_WIDTH {
            compute_hashes_unrolled_8(top_row, middle_row, bottom_row, output, x, initial);
            x += 8;
        }

        if x + 6 <= INTERIOR_WIDTH {
            compute_hashes_unrolled_6(top_row, middle_row, bottom_row, output, x, initial);
            x += 6;
        }

        while x < INTERIOR_WIDTH {
            *output.add(x) = compute_hash_vectorized(
                initial,
                *top_row.add(x),
                *top_row.add(x + 1),
                *top_row.add(x + 2),
                *middle_row.add(x),
                *middle_row.add(x + 1),
                *middle_row.add(x + 2),
                *bottom_row.add(x),
                *bottom_row.add(x + 1),
                *bottom_row.add(x + 2),
            );
            x += 1;
        }
    }
}

/// Compute signature for a single 3x3 matrix (center pixel)
/// Returns the signature of the center pixel
#[no_mangle]
pub extern "C" fn compute_signature_3x3(
    source: *const u32,
    seed: u64
) -> u64 {
    if source.is_null() {
        return 0;
    }

    let initial = compute_seeded_hash(seed);

    unsafe {
        // Load 3x3 grid values
        let tl = *source.add(0); let tc = *source.add(1); let tr = *source.add(2);
        let ml = *source.add(3); let mc = *source.add(4); let mr = *source.add(5);
        let bl = *source.add(6); let bc = *source.add(7); let br = *source.add(8);

        compute_hash_vectorized(initial, tl, tc, tr, ml, mc, mr, bl, bc, br)
    }
}

/// Compute signatures for all pixels in a 2D array
/// Only processes interior pixels (excluding 1-pixel border)
#[no_mangle]
pub extern "C" fn compute_signatures(
    source: *const u32,
    destination: *mut u64,
    width: c_int,
    height: c_int,
    seed: u64
) {
    if source.is_null() || destination.is_null() || width < 3 || height < 3 {
        return;
    }

    let width = width as usize;
    let height = height as usize;
    let interior_width = width - 2;
    let initial = compute_seeded_hash(seed);

    unsafe {
        if width == 64 && height == 64 {
            compute_signatures_64x64(source, destination, initial);
            return;
        }

        // Process each interior pixel (skip 1-pixel border)
        for y in 1..(height - 1) {
            let top_row = source.add((y - 1) * width);
            let middle_row = top_row.add(width);
            let bottom_row = middle_row.add(width);
            let output = destination.add(y * width + 1);
            let mut x = 0usize;

            while x + 4 <= interior_width {
                compute_hashes_unrolled_4(top_row, middle_row, bottom_row, output, x, initial);
                x += 4;
            }

            while x < interior_width {
                *output.add(x) = compute_hash_vectorized(
                    initial,
                    *top_row.add(x),
                    *top_row.add(x + 1),
                    *top_row.add(x + 2),
                    *middle_row.add(x),
                    *middle_row.add(x + 1),
                    *middle_row.add(x + 2),
                    *bottom_row.add(x),
                    *bottom_row.add(x + 1),
                    *bottom_row.add(x + 2),
                );
                x += 1;
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn compute_signatures_reference(source: &[u32], destination: &mut [u64], width: usize, height: usize, seed: u64) {
        for y in 1..(height - 1) {
            for x in 1..(width - 1) {
                let mut hash = compute_seeded_hash(seed);

                hash ^= source[(y - 1) * width + (x - 1)] as u64; hash = hash.wrapping_mul(P3);
                hash ^= source[(y - 1) * width + x] as u64;       hash = hash.wrapping_mul(P4);
                hash ^= source[(y - 1) * width + (x + 1)] as u64; hash = hash.wrapping_mul(P5);
                hash ^= source[y * width + (x - 1)] as u64;       hash = hash.wrapping_mul(P6);
                hash ^= source[y * width + x] as u64;             hash = hash.wrapping_mul(P7);
                hash ^= source[y * width + (x + 1)] as u64;       hash = hash.wrapping_mul(P8);
                hash ^= source[(y + 1) * width + (x - 1)] as u64; hash = hash.wrapping_mul(P9);
                hash ^= source[(y + 1) * width + x] as u64;       hash = hash.wrapping_mul(P10);
                hash ^= source[(y + 1) * width + (x + 1)] as u64; hash = hash.wrapping_mul(P11);

                destination[y * width + x] = hash;
            }
        }
    }

    fn generate_test_data(width: usize, height: usize) -> Vec<u32> {
        let mut data = vec![0u32; width * height];
        let mut state = 42u32;

        for y in 0..height {
            for x in 0..width {
                let index = y * width + x;
                data[index] = if (x + y) % 4 == 0 {
                    1 + ((x * y) % 10) as u32
                } else if (x * y) % 7 == 0 {
                    state = state.wrapping_mul(1_664_525).wrapping_add(1_013_904_223);
                    1 + (state % 99)
                } else {
                    ((x + (y * 2)) % 50) as u32
                };
            }
        }

        data
    }

    #[test]
    fn test_compute_signature_3x3() {
        let source = [1u32, 2, 3, 4, 5, 6, 7, 8, 9];
        let signature = compute_signature_3x3(source.as_ptr(), DEFAULT_SEED);
        assert_ne!(signature, 0);
    }

    #[test]
    fn test_compute_signatures() {
        let source = [
            1u32, 2, 3, 4,
            5, 6, 7, 8,
            9, 10, 11, 12,
            13, 14, 15, 16
        ];
        let mut destination = [0u64; 16];
        
        compute_signatures(source.as_ptr(), destination.as_mut_ptr(), 4, 4, DEFAULT_SEED);
        
        // Only interior pixels should have non-zero signatures
        assert_eq!(destination[0], 0); // corner
        assert_ne!(destination[5], 0); // interior (1,1)
        assert_ne!(destination[6], 0); // interior (1,2)
        assert_ne!(destination[9], 0); // interior (2,1)
        assert_ne!(destination[10], 0); // interior (2,2)
    }

    #[test]
    fn test_identical_inputs_produce_same_signature() {
        let source1 = [1u32, 2, 3, 4, 5, 6, 7, 8, 9];
        let source2 = [1u32, 2, 3, 4, 5, 6, 7, 8, 9];
        
        let sig1 = compute_signature_3x3(source1.as_ptr(), DEFAULT_SEED);
        let sig2 = compute_signature_3x3(source2.as_ptr(), DEFAULT_SEED);
        
        assert_eq!(sig1, sig2);
    }

    #[test]
    fn test_different_seeds_produce_different_signatures() {
        let source = [1u32, 2, 3, 4, 5, 6, 7, 8, 9];
        
        let sig1 = compute_signature_3x3(source.as_ptr(), 12345);
        let sig2 = compute_signature_3x3(source.as_ptr(), 54321);
        
        assert_ne!(sig1, sig2);
    }

    #[test]
    fn test_compute_signatures_matches_reference_across_sizes() {
        for (width, height) in [(3usize, 3usize), (4, 4), (16, 16), (64, 64)] {
            let source = generate_test_data(width, height);
            let mut actual = vec![u64::MAX; width * height];
            let mut expected = vec![u64::MAX; width * height];

            compute_signatures(
                source.as_ptr(),
                actual.as_mut_ptr(),
                width as c_int,
                height as c_int,
                DEFAULT_SEED,
            );
            compute_signatures_reference(&source, &mut expected, width, height, DEFAULT_SEED);

            assert_eq!(actual, expected, "mismatch for {width}x{height}");
        }
    }

    #[test]
    fn test_compute_signatures_matches_reference_with_alternate_seed() {
        let width = 64usize;
        let height = 64usize;
        let seed = 12345u64;
        let source = generate_test_data(width, height);
        let mut actual = vec![0xDEADBEEFDEADBEEF; width * height];
        let mut expected = vec![0xDEADBEEFDEADBEEF; width * height];

        compute_signatures(
            source.as_ptr(),
            actual.as_mut_ptr(),
            width as c_int,
            height as c_int,
            seed,
        );
        compute_signatures_reference(&source, &mut expected, width, height, seed);

        assert_eq!(actual, expected);
    }
}
