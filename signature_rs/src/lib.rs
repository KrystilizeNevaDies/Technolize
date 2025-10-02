use std::os::raw::{c_int, c_uint};

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

const DEFAULT_SEED: u64 = 67890;

/// Computes a hash signature for a 3x3 neighborhood
fn compute_hash_vectorized(
    tl: u32, tc: u32, tr: u32,
    ml: u32, mc: u32, mr: u32, 
    bl: u32, bc: u32, br: u32,
    seed: u64
) -> u64 {
    let mut result = P1;
    result ^= seed;
    result = result.wrapping_mul(P2);

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

    unsafe {
        // Load 3x3 grid values
        let tl = *source.add(0); let tc = *source.add(1); let tr = *source.add(2);
        let ml = *source.add(3); let mc = *source.add(4); let mr = *source.add(5);
        let bl = *source.add(6); let bc = *source.add(7); let br = *source.add(8);
        
        compute_hash_vectorized(tl, tc, tr, ml, mc, mr, bl, bc, br, seed)
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

    unsafe {
        // Process each interior pixel (skip 1-pixel border)
        for y in 1..(height - 1) {
            for x in 1..(width - 1) {
                let current_pixel_index = y * width + x;
                let base_index = (y - 1) * width + (x - 1);
                
                // Load 3x3 neighborhood
                let tl = *source.add(base_index);
                let tc = *source.add(base_index + 1);
                let tr = *source.add(base_index + 2);
                let ml = *source.add(base_index + width);
                let mc = *source.add(base_index + width + 1);
                let mr = *source.add(base_index + width + 2);
                let bl = *source.add(base_index + 2 * width);
                let bc = *source.add(base_index + 2 * width + 1);
                let br = *source.add(base_index + 2 * width + 2);

                let signature = compute_hash_vectorized(tl, tc, tr, ml, mc, mr, bl, bc, br, seed);
                *destination.add(current_pixel_index) = signature;
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

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
}
