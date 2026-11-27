# Fixture: the lines of start-ninfer.ps1 the catalog builder reads.
$IQ4XS = 'models\inkling2-iq4xs-20261124\Inkling-2-Mini-UD-IQ4_XS-ninfer-v3.ninfer'
$Q8KXL = 'models\inkling2-q8kxl-20261124\Inkling-2-Mini-UD-Q8_K_XL-ninfer-v3.ninfer'
$Inkling = @{
    'inkling2-mini'      = @{ File = $IQ4XS; Context = 262144; Gpus = '' }
    'inkling2-mini-q8'   = @{ File = $Q8KXL; Context = 524288; Gpus = "$TwoGpus --tensor-split 1,3" }
}
