use serde::{Deserialize, Deserializer, de::Error};

#[derive(Deserialize)]
#[serde(untagged)]
enum U64Wire {
    Number(u64),
    String(String),
}

impl U64Wire {
    fn into_value<E: Error>(self) -> Result<u64, E> {
        match self {
            Self::Number(value) if value <= 9_007_199_254_740_991 => Ok(value),
            Self::Number(_) => Err(E::custom(
                "JSON numeric u64 exceeds the cross-language safe-integer range",
            )),
            Self::String(value)
                if value == "0"
                    || (!value.starts_with('0')
                        && value.bytes().all(|byte| byte.is_ascii_digit())) =>
            {
                value.parse().map_err(E::custom)
            }
            Self::String(_) => Err(E::custom("u64 string must be canonical decimal")),
        }
    }
}

pub(crate) fn deserialize_u64<'de, D: Deserializer<'de>>(deserializer: D) -> Result<u64, D::Error> {
    U64Wire::deserialize(deserializer)?.into_value()
}

pub(crate) fn deserialize_optional_u64<'de, D: Deserializer<'de>>(
    deserializer: D,
) -> Result<Option<u64>, D::Error> {
    Option::<U64Wire>::deserialize(deserializer)?
        .map(U64Wire::into_value)
        .transpose()
}
